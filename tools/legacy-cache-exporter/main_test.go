package main

import (
	"encoding/binary"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
	"time"

	bolt "go.etcd.io/bbolt"
)

func TestBuildExportReadsOnlyKnownBucketsAndPreservesWireEncoding(t *testing.T) {
	path := filepath.Join(t.TempDir(), "cache.bolt")
	database, err := bolt.Open(path, 0o600, nil)
	if err != nil {
		t.Fatal(err)
	}
	err = database.Update(func(transaction *bolt.Tx) error {
		known, createErr := transaction.CreateBucket([]byte("mikan"))
		if createErr != nil {
			return createErr
		}
		value := make([]byte, 8, 32)
		binary.LittleEndian.PutUint64(value, uint64(2_000_000_000))
		value = append(value, []byte(`{"Params":{"Values":[123]}}`)...)
		if putErr := known.Put([]byte(`["https://example.invalid/item"]`), value); putErr != nil {
			return putErr
		}
		_, createErr = transaction.CreateBucket([]byte("private_extension"))
		return createErr
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := database.Close(); err != nil {
		t.Fatal(err)
	}

	data, buckets, entries, unknown, err := buildExport(
		[]sourceDatabase{{name: "bolt", path: path}},
		time.Date(2026, 8, 8, 1, 2, 3, 4_000, time.UTC))
	if err != nil {
		t.Fatal(err)
	}
	if buckets != 1 || entries != 1 || unknown != 1 {
		t.Fatalf("unexpected counts: buckets=%d entries=%d unknown=%d", buckets, entries, unknown)
	}
	var result exportPackage
	if err := json.Unmarshal(data, &result); err != nil {
		t.Fatal(err)
	}
	if result.ExportedAtUTC != "2026-08-08T01:02:03.0000040Z" {
		t.Fatalf("unexpected timestamp: %s", result.ExportedAtUTC)
	}
	entry := result.Databases[0].Buckets[0].Entries[0]
	if string(entry.KeyJSON) != `["https://example.invalid/item"]` {
		t.Fatalf("unexpected key: %s", entry.KeyJSON)
	}
	if entry.ExpiresAtUnixSeconds != 2_000_000_000 {
		t.Fatalf("unexpected expiration: %d", entry.ExpiresAtUnixSeconds)
	}
}

func TestBuildExportRejectsMalformedKnownValue(t *testing.T) {
	path := filepath.Join(t.TempDir(), "cache.bolt")
	database, err := bolt.Open(path, 0o600, nil)
	if err != nil {
		t.Fatal(err)
	}
	err = database.Update(func(transaction *bolt.Tx) error {
		bucket, createErr := transaction.CreateBucket([]byte("bangumi"))
		if createErr != nil {
			return createErr
		}
		return bucket.Put([]byte(`123`), []byte("short"))
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := database.Close(); err != nil {
		t.Fatal(err)
	}

	if _, _, _, _, err := buildExport(
		[]sourceDatabase{{name: "bolt", path: path}},
		time.Now()); err == nil {
		t.Fatal("expected malformed value to fail")
	}
}

func TestWriteNewFileAtomicallyRefusesOverwrite(t *testing.T) {
	path := filepath.Join(t.TempDir(), "export.json")
	if err := writeNewFileAtomically(path, []byte("first")); err != nil {
		t.Fatal(err)
	}
	if err := writeNewFileAtomically(path, []byte("second")); err == nil {
		t.Fatal("expected overwrite refusal")
	}
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if string(data) != "first" {
		t.Fatalf("existing output changed: %q", data)
	}
}
