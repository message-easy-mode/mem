import "@testing-library/jest-dom/vitest"

// Keep browser-like test behaviour deterministic. Individual tests opt into
// MSW handlers rather than relying on an application-wide mocked API surface.
Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false,
  }),
})

// Radix primitives used by the Matrix user form observe layout changes.
// JSDOM does not implement ResizeObserver, so provide the smallest browser-like
// implementation needed by component tests without changing application behavior.
if (typeof globalThis.ResizeObserver === "undefined") {
  class ResizeObserverMock {
    constructor(_callback: ResizeObserverCallback) {}

    observe(_target: Element, _options?: ResizeObserverOptions) {}

    unobserve(_target: Element) {}

    disconnect() {}
  }

  Object.defineProperty(globalThis, "ResizeObserver", {
    configurable: true,
    writable: true,
    value: ResizeObserverMock,
  })
}
