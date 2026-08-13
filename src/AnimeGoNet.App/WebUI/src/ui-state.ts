export type UiRegionState = "loading" | "ready" | "empty" | "error";

export interface PolledRegionRefreshState {
  background: boolean;
  signatureChanged: boolean;
  hasExpandedContent: boolean;
  hasFocusedEditor: boolean;
  hasOpenDialog: boolean;
}

export function shouldReplacePolledRegion(
  state: PolledRegionRefreshState,
): boolean {
  if (!state.background) return true;
  return state.signatureChanged
    && !state.hasExpandedContent
    && !state.hasFocusedEditor
    && !state.hasOpenDialog;
}

export function setRegionState(
  region: HTMLElement,
  state: UiRegionState,
): void {
  region.dataset.uiState = state;
  region.setAttribute("aria-busy", String(state === "loading"));
  if (!region.hasAttribute("aria-live")) {
    region.setAttribute("aria-live", "polite");
  }
}

export function renderRegionMessage(
  region: HTMLElement,
  state: Exclude<UiRegionState, "ready">,
  message: string,
): HTMLParagraphElement {
  setRegionState(region, state);
  const node = region.ownerDocument.createElement("p");
  node.className = `ui-state ui-state--${state} muted empty`;
  node.textContent = message;
  node.setAttribute("role", state === "error" ? "alert" : "status");
  node.setAttribute("aria-atomic", "true");
  region.replaceChildren(node);
  return node;
}

export function renderRegionContent(
  region: HTMLElement,
  ...content: Node[]
): void {
  setRegionState(region, "ready");
  region.replaceChildren(...content);
}
