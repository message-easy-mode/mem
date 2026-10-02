/**
 * Password rotation invalidates the current browser authority. Use a hard
 * history-replacing navigation so React cannot briefly render the previously
 * authenticated page and the next session check starts from a fresh document.
 */
export function replaceWithFreshOperatorLogin() {
  window.location.replace("/login")
}
