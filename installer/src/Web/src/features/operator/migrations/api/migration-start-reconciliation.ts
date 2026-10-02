/**
 * A start POST may lose its response after a durable operation was accepted.
 * Observe before posting so an old failed/completed attempt cannot be mistaken
 * for this start. Reconcile by GET only; never automatically replay a mutation.
 * The caller must distinguish authoritative HTTP problems (including step-up)
 * from an ambiguous transport/response failure.
 */
export async function startMigrationOperationWithReconciliation<T>({
  list,
  start,
  identity,
  matches,
  isHttpProblem,
}: {
  list: () => Promise<T[]>
  start: () => Promise<T>
  identity: (operation: T) => string
  matches: (operation: T) => boolean
  isHttpProblem: (error: unknown) => boolean
}): Promise<T> {
  // If the initial observation fails, no POST is sent. Do not silently proceed
  // without the evidence needed to disambiguate historical attempts.
  const before = new Set((await list()).map(identity))

  try {
    return await start()
  } catch (caught) {
    if (isHttpProblem(caught)) throw caught

    // Three read attempts accommodate brief connection loss or a row becoming
    // visible just after the response is lost. These are NOT mutation retries.
    for (const delay of [0, 250, 750]) {
      if (delay > 0) await new Promise<void>((resolve) => setTimeout(resolve, delay))
      try {
        const candidates = (await list()).filter(
          (operation) => !before.has(identity(operation)) && matches(operation),
        )
        // A failed new operation is also authoritative: return its real state
        // so the workspace renders its failure, not a misleading fetch error.
        if (candidates.length === 1) return candidates[0]
      } catch {
        // No state was established. Keep the original POST error if subsequent
        // reads cannot identify exactly one matching durable operation.
      }
    }

    throw caught
  }
}
