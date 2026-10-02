import { afterEach, describe, expect, it, vi } from 'vitest'
import { login, readHostStatus, subscribeToSessionExpiry } from './api'

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('Source Assistant API session expiry handling', () => {
  it('notifies the application when a protected request returns 401', async () => {
    const expired = vi.fn()
    const unsubscribe = subscribeToSessionExpiry(expired)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(jsonResponse({
      title: 'Authentication required',
      detail: 'Sign in again.',
    }, 401)))

    await expect(readHostStatus()).rejects.toThrow('Sign in again.')
    expect(expired).toHaveBeenCalledOnce()

    unsubscribe()
  })

  it('does not treat an invalid access code as an expired authenticated session', async () => {
    const expired = vi.fn()
    const unsubscribe = subscribeToSessionExpiry(expired)
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(jsonResponse({
      title: 'Access denied',
      detail: 'The access code was not accepted.',
    }, 401)))

    await expect(login('WRONG-CODE')).rejects.toThrow('The access code was not accepted.')
    expect(expired).not.toHaveBeenCalled()

    unsubscribe()
  })
})
