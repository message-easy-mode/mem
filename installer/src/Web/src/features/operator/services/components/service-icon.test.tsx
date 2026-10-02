import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { ServiceIcon } from "./service-icon"

describe("ServiceIcon", () => {
  it("uses a blue-green treatment for managed chat-server stacks", () => {
    const { container } = render(<ServiceIcon serviceName="stack-test-stack" />)
    const wrapper = container.firstElementChild
    const icon = wrapper?.querySelector("svg")

    expect(wrapper).toHaveClass(
      "border-sky-500/30",
      "bg-gradient-to-br",
      "from-sky-500/15",
      "to-emerald-500/15",
    )
    expect(icon).toHaveClass("text-emerald-600", "dark:text-emerald-300")
  })

  it("keeps restore staging deliberately neutral and monochrome", () => {
    const { container } = render(
      <ServiceIcon serviceName="restore-staging-20260919-020723Z-999abb70" />,
    )
    const wrapper = container.firstElementChild
    const icon = wrapper?.querySelector("svg")

    expect(wrapper).toHaveClass("border-border", "bg-background/60")
    expect(wrapper).not.toHaveClass("bg-gradient-to-br")
    expect(icon).toHaveClass("text-foreground")
  })
})
