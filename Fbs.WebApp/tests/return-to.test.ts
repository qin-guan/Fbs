import { describe, expect, it } from 'vitest'
import { safeReturnTo } from '../lib/return-to'

describe('where to go back to after signing in', () => {
  it('is the page somebody was on, with its query and hash', () => {
    expect(safeReturnTo('/t/3sib/bookings?mine=true#today')).toBe('/t/3sib/bookings?mine=true#today')
    expect(safeReturnTo('/')).toBe('/')
  })

  it('is never another site', () => {
    for (const value of ['https://evil.example/', '//evil.example/path', '/\\evil.example', 'javascript:alert(1)', 'evil.example', '/%2F%2Fevil.example', '/\t/evil.example', '/\n/evil.example']) {
      const result = safeReturnTo(value)
      expect(result.startsWith('/')).toBe(true)
      expect(result.startsWith('//')).toBe(false)
      expect(new URL(result, 'https://app.example').origin).toBe('https://app.example')
    }
  })

  it('is the home page when there is nothing to go back to', () => {
    for (const value of [undefined, null, 42, {}, '']) {
      expect(safeReturnTo(value)).toBe('/')
    }
  })
})
