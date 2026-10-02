import { describe, expect, it } from "vitest"

import { getMemReleaseCodename, MEM_0_2_0_CODENAME } from "./runtime-release-identity"

describe("runtime release identity", () => {
  it.each([
    "0.2.0",
    "0.2.0-rc.5.qa.5",
    "0.2.0-test",
    "v0.2.0+0123456789ab",
  ])("maps %s to the 0.2.0 codename", (version) => {
    expect(getMemReleaseCodename(version)).toBe(MEM_0_2_0_CODENAME)
  })

  it.each(["0.1.0", "0.2.1", "0.3.0", "1.0.0", ""]) (
    "does not invent the 0.2.0 codename for %s",
    (version) => {
      expect(getMemReleaseCodename(version)).toBeNull()
    },
  )
})
