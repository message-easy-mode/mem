import { Globe2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { FormField } from "@/features/shared/domains/components/ingress-tls-shared"
import type {
  ProxyHostForm,
  ProxyHostFormValues,
} from "@/features/shared/domains/components/types"

export function ProxyHostTestCard({
  form,
  disabled,
  isPending,
  onSubmit,
}: {
  form: ProxyHostForm
  disabled: boolean
  isPending: boolean
  onSubmit: (values: ProxyHostFormValues) => void
}) {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("operatorDomains.certificates.proxy.title")}</CardTitle>
        <CardDescription>
          {t("operatorDomains.certificates.proxy.description")}
        </CardDescription>
      </CardHeader>

      <CardContent>
        <form className="space-y-4" onSubmit={form.handleSubmit(onSubmit)}>
          <div className="grid gap-3 md:grid-cols-2">
            <FormField label={t("operatorDomains.certificates.proxy.domain")}>
              <Input {...form.register("domain")} />
            </FormField>

            <FormField label={t("operatorDomains.certificates.proxy.forwardHost")}>
              <Input {...form.register("forwardHost")} />
            </FormField>

            <FormField label={t("operatorDomains.certificates.proxy.forwardPort")}>
              <Input
                type="number"
                {...form.register("forwardPort", { valueAsNumber: true })}
              />
            </FormField>

            <FormField label={t("operatorDomains.certificates.proxy.forwardScheme")}>
              <Input {...form.register("forwardScheme")} />
            </FormField>
          </div>

          <Button type="submit" disabled={disabled}>
            <Globe2 className="mr-2 h-4 w-4" />
            {isPending
              ? t("operatorDomains.certificates.proxy.verifying")
              : t("operatorDomains.certificates.proxy.verify")}
          </Button>
        </form>
      </CardContent>
    </Card>
  )
}
