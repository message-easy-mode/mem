export const MEM_0_2_0_CODENAME = "baby-fish"

export function getMemReleaseCodename(version: string | null | undefined) {
  const normalized = version?.trim().replace(/^v/i, "") ?? ""

  return /^0\.2\.0(?:$|[-+])/.test(normalized)
    ? MEM_0_2_0_CODENAME
    : null
}
