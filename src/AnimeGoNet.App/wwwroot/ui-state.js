export function shouldReplacePolledRegion(state) {
    if (!state.background)
        return true;
    return state.signatureChanged
        && !state.hasExpandedContent
        && !state.hasFocusedEditor
        && !state.hasOpenDialog;
}
export function setRegionState(region, state) {
    region.dataset.uiState = state;
    region.setAttribute("aria-busy", String(state === "loading"));
    if (!region.hasAttribute("aria-live")) {
        region.setAttribute("aria-live", "polite");
    }
}
export function renderRegionMessage(region, state, message) {
    setRegionState(region, state);
    const node = region.ownerDocument.createElement("p");
    node.className = `ui-state ui-state--${state} muted empty`;
    node.textContent = message;
    node.setAttribute("role", state === "error" ? "alert" : "status");
    node.setAttribute("aria-atomic", "true");
    region.replaceChildren(node);
    return node;
}
export function renderRegionContent(region, ...content) {
    setRegionState(region, "ready");
    region.replaceChildren(...content);
}
