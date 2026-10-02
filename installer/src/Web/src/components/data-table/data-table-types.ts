/* eslint-disable @typescript-eslint/no-unused-vars */
import "@tanstack/react-table"

declare module "@tanstack/react-table" {
  interface ColumnMeta<TData, TValue> {
    label?: string
    align?: "left" | "center" | "right"
  }
}

export {}
