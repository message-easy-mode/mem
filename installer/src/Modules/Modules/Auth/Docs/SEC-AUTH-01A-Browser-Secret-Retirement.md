# SEC-AUTH-01A — Browser Shared-Secret Retirement

## Status

Implemented as a transitional bridge while SEC-AUTH-02/03 introduces named ASP.NET Core Identity operators.

## Outcome

Browser code no longer retrieves, stores, injects, or requires a Host Agent infrastructure secret.

The normal current browser flow is now:

```text
setup token unlock
  → existing same-origin installer cookie
  → server validates the transitional unlock claim
  → Host Agent endpoint adapter
```

The browser does **not** send `X-MEM-Agent-Secret`.

## Retired browser authority paths

- `VITE_MEM_AGENT_SECRET`
- `window.localStorage["mem.agentSecret"]`
- browser `X-MEM-Agent-Secret` request headers
- `MEM_E2E_AGENT_SECRET`
- E2E browser storage injection
- Swagger development API-key input for the Host Agent shared secret
- development `HostAgent.InternalCommandSecret` configuration fallback

## Server-side transitional guard

`HostAgentEndpointOperatorGuard` requires both:

1. an authenticated server-side cookie session; and
2. the existing `mem_installer_unlocked=true` claim issued only by the current installer unlock endpoint.

This remains a deliberately coarse transitional control. It is **not** named-user identity, MFA, policy authorization, CSRF protection, session revocation, or Host Agent isolation.

SEC-AUTH-02/03 replaces this claim-based generic session with ASP.NET Core Identity, named operators, first-owner bootstrap, and explicit policies.

## Scope boundary

This change removes browser authority. It does not yet complete the CLI/local Host Agent credential redesign. Any residual CLI header option is no longer accepted as browser/API authority by the endpoint adapters and is addressed explicitly in the Host Agent isolation follow-on programme.

## Verification expectation

The browser-source audit must return no matches for:

```bash
grep -RInE 'VITE_MEM_AGENT_SECRET|mem\.agentSecret|X-MEM-Agent-Secret|MEM_E2E_AGENT_SECRET|dev-only-change-me' \
  installer/src/Web \
  --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=coverage
```

The only valid current browser E2E inputs are the base URL and disposable installer setup token. Browser tests must prove the catalog request does not carry `X-MEM-Agent-Secret`.
