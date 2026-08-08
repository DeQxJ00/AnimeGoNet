package main

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"time"

	bolt "go.etcd.io/bbolt"
)

const (
	formatName     = "animego-legacy-cache"
	formatVersion  = 1
	sourceCommit   = "develop@c7475dfc55a374cd0dd08821bf17125dab1e3145"
	maxPackageSize = 64 * 1024 * 1024
	maxEntries     = 50_000
	maxKeySize     = 4096
	maxValueSize   = 8 * 1024 * 1024
)

var knownBuckets = map[string][]string{
	"bolt":     {"bangumi", "hash2entity", "mikan", "name2hash", "themoviedb"},
	"bolt_sub": {"bangumi_sub"},
}

type exportPackage struct {
	Format        string           `json:"format"`
	Version       int              `json:"version"`
	SourceCommit  string           `json:"source_commit"`
	ExportedAtUTC string           `json:"exported_at_utc"`
	Databases     []exportDatabase `json:"databases"`
}

type exportDatabase struct {
	Name    string         `json:"name"`
	Buckets []exportBucket `json:"buckets"`
}

type exportBucket struct {
	Name    string        `json:"name"`
	Entries []exportEntry `json:"entries"`
}

type exportEntry struct {
	KeyJSON              json.RawMessage `json:"key_json"`
	ValueJSON            json.RawMessage `json:"value_json"`
	ExpiresAtUnixSeconds int64           `json:"expires_at_unix_seconds"`
}

type sourceDatabase struct {
	name string
	path string
}

func main() {
	boltPath := flag.String("bolt", "", "path to the old main Bolt database")
	boltSubPath := flag.String("bolt-sub", "", "path to the old Bangumi archive Bolt database")
	outputPath := flag.String("output", "", "new JSON output path (must not already exist)")
	flag.Parse()
	if flag.NArg() != 0 || *outputPath == "" || (*boltPath == "" && *boltSubPath == "") {
		flag.Usage()
		os.Exit(2)
	}

	sources := make([]sourceDatabase, 0, 2)
	if *boltPath != "" {
		sources = append(sources, sourceDatabase{name: "bolt", path: *boltPath})
	}
	if *boltSubPath != "" {
		sources = append(sources, sourceDatabase{name: "bolt_sub", path: *boltSubPath})
	}
	data, bucketCount, entryCount, unknownCount, err := buildExport(sources, time.Now())
	if err != nil {
		fmt.Fprintln(os.Stderr, "legacy_cache_export_failed:", err)
		os.Exit(1)
	}
	if err := writeNewFileAtomically(*outputPath, data); err != nil {
		fmt.Fprintln(os.Stderr, "legacy_cache_export_write_failed:", err)
		os.Exit(1)
	}
	fmt.Fprintf(
		os.Stderr,
		"exported %d known buckets and %d entries; ignored %d unknown buckets\n",
		bucketCount,
		entryCount,
		unknownCount)
}

func buildExport(sources []sourceDatabase, exportedAt time.Time) ([]byte, int, int, int, error) {
	if len(sources) < 1 || len(sources) > 2 {
		return nil, 0, 0, 0, errors.New("one or two source databases are required")
	}
	seen := make(map[string]struct{}, len(sources))
	databaseResults := make([]exportDatabase, 0, len(sources))
	bucketCount := 0
	entryCount := 0
	unknownCount := 0
	for _, source := range sources {
		buckets, ok := knownBuckets[source.name]
		if !ok {
			return nil, 0, 0, 0, errors.New("unsupported source database name")
		}
		if _, duplicated := seen[source.name]; duplicated {
			return nil, 0, 0, 0, errors.New("duplicate source database name")
		}
		seen[source.name] = struct{}{}

		database, err := bolt.Open(source.path, 0o600, &bolt.Options{
			ReadOnly: true,
			Timeout:  time.Second,
		})
		if err != nil {
			return nil, 0, 0, 0, fmt.Errorf("open %s database: %w", source.name, err)
		}
		result, unknown, err := readDatabase(database, source.name, buckets, &entryCount)
		closeErr := database.Close()
		if err != nil {
			return nil, 0, 0, 0, err
		}
		if closeErr != nil {
			return nil, 0, 0, 0, fmt.Errorf("close %s database: %w", source.name, closeErr)
		}
		unknownCount += unknown
		bucketCount += len(result.Buckets)
		databaseResults = append(databaseResults, result)
	}
	sort.Slice(databaseResults, func(i, j int) bool {
		return databaseResults[i].Name < databaseResults[j].Name
	})

	result := exportPackage{
		Format:        formatName,
		Version:       formatVersion,
		SourceCommit:  sourceCommit,
		ExportedAtUTC: exportedAt.UTC().Format("2006-01-02T15:04:05.0000000Z"),
		Databases:     databaseResults,
	}
	var output bytes.Buffer
	encoder := json.NewEncoder(&output)
	encoder.SetEscapeHTML(false)
	if err := encoder.Encode(result); err != nil {
		return nil, 0, 0, 0, fmt.Errorf("encode export package: %w", err)
	}
	if output.Len() > maxPackageSize {
		return nil, 0, 0, 0, fmt.Errorf("export package exceeds %d bytes", maxPackageSize)
	}
	return output.Bytes(), bucketCount, entryCount, unknownCount, nil
}

func readDatabase(
	database *bolt.DB,
	databaseName string,
	bucketNames []string,
	totalEntries *int,
) (exportDatabase, int, error) {
	result := exportDatabase{Name: databaseName, Buckets: make([]exportBucket, 0, len(bucketNames))}
	known := make(map[string]struct{}, len(bucketNames))
	for _, name := range bucketNames {
		known[name] = struct{}{}
	}
	unknownCount := 0
	err := database.View(func(transaction *bolt.Tx) error {
		if err := transaction.ForEach(func(name []byte, _ *bolt.Bucket) error {
			if _, ok := known[string(name)]; !ok {
				unknownCount++
			}
			return nil
		}); err != nil {
			return err
		}
		for _, bucketName := range bucketNames {
			bucket := transaction.Bucket([]byte(bucketName))
			if bucket == nil {
				continue
			}
			exported := exportBucket{Name: bucketName, Entries: make([]exportEntry, 0)}
			if err := bucket.ForEach(func(key, value []byte) error {
				if value == nil {
					return errors.New("known bucket contains a nested bucket")
				}
				if len(key) > maxKeySize || !json.Valid(key) {
					return errors.New("known bucket contains an invalid or oversized JSON key")
				}
				if len(value) < 8 || len(value)-8 > maxValueSize || !json.Valid(value[8:]) {
					return errors.New("known bucket contains an invalid or oversized encoded value")
				}
				expires := int64(binary.LittleEndian.Uint64(value[:8]))
				if expires < 0 {
					return errors.New("known bucket contains an invalid expiration timestamp")
				}
				(*totalEntries)++
				if *totalEntries > maxEntries {
					return fmt.Errorf("export contains more than %d entries", maxEntries)
				}
				exported.Entries = append(exported.Entries, exportEntry{
					KeyJSON:              append(json.RawMessage(nil), key...),
					ValueJSON:            append(json.RawMessage(nil), value[8:]...),
					ExpiresAtUnixSeconds: expires,
				})
				return nil
			}); err != nil {
				return fmt.Errorf("read %s known bucket: %w", databaseName, err)
			}
			result.Buckets = append(result.Buckets, exported)
		}
		return nil
	})
	if err != nil {
		return exportDatabase{}, 0, err
	}
	return result, unknownCount, nil
}

func writeNewFileAtomically(path string, data []byte) error {
	absolute, err := filepath.Abs(path)
	if err != nil {
		return errors.New("resolve output path")
	}
	if _, err := os.Stat(absolute); err == nil {
		return errors.New("output file already exists")
	} else if !errors.Is(err, os.ErrNotExist) {
		return errors.New("inspect output path")
	}
	temporary := fmt.Sprintf("%s.tmp-%d", absolute, os.Getpid())
	file, err := os.OpenFile(temporary, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0o600)
	if err != nil {
		return errors.New("create temporary output")
	}
	keep := false
	defer func() {
		_ = file.Close()
		if !keep {
			_ = os.Remove(temporary)
		}
	}()
	if _, err := file.Write(data); err != nil {
		return errors.New("write temporary output")
	}
	if err := file.Sync(); err != nil {
		return errors.New("flush temporary output")
	}
	if err := file.Close(); err != nil {
		return errors.New("close temporary output")
	}
	if err := os.Link(temporary, absolute); err != nil {
		return errors.New("publish output")
	}
	if err := os.Remove(temporary); err != nil {
		return errors.New("remove temporary output after publish")
	}
	keep = true
	return nil
}
