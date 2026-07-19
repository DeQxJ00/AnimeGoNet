interface RuntimeStatus {
  database_schema_version: number;
  native_aot: boolean;
  runtime_identifier: string;
  paths: { data_path: string };
  capabilities: Record<string, boolean>;
}

const accessKey = new URLSearchParams(window.location.search).get("access_key");
const headers = new Headers();
if (accessKey) headers.set("Access-Key", accessKey);

async function loadStatus(): Promise<void> {
  const health = document.querySelector<HTMLElement>("#health");
  try {
    const response = await fetch("/api/v1/status", { headers });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const status = await response.json() as RuntimeStatus;
    document.querySelector<HTMLElement>("#schema")!.textContent = `v${status.database_schema_version}`;
    document.querySelector<HTMLElement>("#runtime")!.textContent = status.native_aot ? `NativeAOT · ${status.runtime_identifier}` : `JIT · ${status.runtime_identifier}`;
    document.querySelector<HTMLElement>("#data-path")!.textContent = status.paths.data_path;
    document.querySelector<HTMLElement>("#modules")!.replaceChildren(...Object.entries(status.capabilities).map(([name, enabled]) => {
      const item = document.createElement("article");
      item.className = `module ${enabled ? "enabled" : ""}`;
      const title = document.createElement("strong");
      title.textContent = name.replaceAll("_", " ");
      const state = document.createElement("span");
      state.textContent = enabled ? "已启用" : "待实现";
      item.append(title, state);
      return item;
    }));
    health!.textContent = "运行中";
    health!.className = "badge ready";
  } catch (error) {
    health!.textContent = error instanceof Error ? error.message : "连接失败";
    health!.className = "badge error";
  }
}

void loadStatus();
