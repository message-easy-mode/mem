import { screen } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { describe, expect, it } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import { DocumentationMarkdown } from "./documentation-markdown"

describe("DocumentationMarkdown", () => {
  it("renders Markdown tables, callouts, heading anchors, and copyable fenced code without enabling raw HTML", () => {
    renderWithProviders(
      <DocumentationMarkdown
        markdown={`## Safe heading

> [!WARNING]
> Check the command before running it.

| Name | State |
| --- | --- |
| docs | ready |

\`\`\`bash
echo safe
\`\`\`

<div data-testid="raw-html">not executable</div>`}
      />,
    )

    expect(screen.getByRole("heading", { name: "Safe heading" })).toHaveAttribute(
      "id",
      "safe-heading",
    )
    expect(screen.getByRole("note")).toHaveTextContent("Check the command before running it.")
    expect(screen.getByRole("table")).toHaveTextContent("docs")
    expect(screen.getByRole("button", { name: "Copy code" })).toBeInTheDocument()
    expect(screen.getByText("<div data-testid=\"raw-html\">not executable</div>")).toBeInTheDocument()
    expect(screen.queryByTestId("raw-html")).not.toBeInTheDocument()
  })

  it("renders an unlabeled fenced block as a copyable code block", () => {
    renderWithProviders(<DocumentationMarkdown markdown={"```\necho unlabeled\n```"} />)

    expect(screen.getByText("echo unlabeled")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Copy code" })).toBeInTheDocument()
  })

  it("does not render unsafe URL schemes as links", () => {
    renderWithProviders(
      <DocumentationMarkdown markdown="[Unsafe](javascript:alert('no')) and [safe](https://example.test/docs)" />,
    )

    expect(screen.queryByRole("link", { name: "Unsafe" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: /safe/i })).toHaveAttribute(
      "rel",
      "noopener noreferrer",
    )
  })

  it("uses pack-provided unique heading anchors and upgrades known local links", () => {
    renderWithProviders(
      <MemoryRouter>
        <DocumentationMarkdown
          markdown={`## Registry mode

[Open installation](installation.md)

## Registry mode`}
          headings={[
            { id: "registry-mode", label: "Registry mode", level: 2 },
            { id: "registry-mode-2", label: "Registry mode", level: 2 },
          ]}
          resolveLocalLink={(href) => (href === "installation.md" ? "/docs/installation" : undefined)}
        />
      </MemoryRouter>,
    )

    expect(screen.getAllByRole("heading", { name: "Registry mode" })).toEqual([
      expect.objectContaining({ id: "registry-mode" }),
      expect.objectContaining({ id: "registry-mode-2" }),
    ])
    expect(screen.getByRole("link", { name: "Open installation" })).toHaveAttribute(
      "href",
      "/docs/installation",
    )
  })
  it("renders portable documentation images through the supplied asset resolver", () => {
    renderWithProviders(
      <DocumentationMarkdown
        markdown="![TURN network path](../../assets/installation/turn-network.png)"
        resolveLocalImage={(source) =>
          source === "../../assets/installation/turn-network.png"
            ? "/assets/turn-network.test.png"
            : undefined
        }
      />,
    )

    expect(screen.getByRole("img", { name: "TURN network path" })).toHaveAttribute(
      "src",
      "/assets/turn-network.test.png",
    )
  })

})
