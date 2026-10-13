import { describe, expect, it } from 'vitest'
import { classifyPress, MIN_DRAG_PX, NEWCOMER_RULE, noteMiss, REGULAR_RULE, TOUCH_HOLD_MS } from '../lib/struggle'

const press = (pointerType: string, heldMs: number, distance: number, cancelled = false) => ({ pointerType, heldMs, distance, cancelled })

describe('a press on the calendar', () => {
  it('with a mouse, picks a time if it is dragged far enough, and nothing if it is a click', () => {
    expect(classifyPress(press('mouse', 120, 0))).toBe('tap')
    expect(classifyPress(press('mouse', 80, MIN_DRAG_PX))).toBe('tap')
    expect(classifyPress(press('mouse', 80, MIN_DRAG_PX + 1))).toBe('drag')
    // A mouse doesn't have to wait first
    expect(classifyPress(press('mouse', 0, 200))).toBe('drag')
    expect(classifyPress(press('pen', 0, 200))).toBe('drag')
  })

  it('with a finger, only drags once it has been held, and scrolls if it moves sooner', () => {
    expect(classifyPress(press('touch', TOUCH_HOLD_MS, 120))).toBe('drag')
    expect(classifyPress(press('touch', TOUCH_HOLD_MS - 1, 120))).toBe('scroll')
  })

  it('with a finger that doesn\'t move, is a tap, or a hold that was let go too soon', () => {
    expect(classifyPress(press('touch', 150, 4))).toBe('tap')
    expect(classifyPress(press('touch', 900, 4))).toBe('hold')
  })

  it('is a scroll whenever the browser took it over, however long it was held', () => {
    expect(classifyPress(press('touch', 900, 120, true))).toBe('scroll')
    expect(classifyPress(press('mouse', 0, 0, true))).toBe('scroll')
  })
})

describe('presses that picked nothing', () => {
  it('mean somebody is stuck once there are enough of them close together', () => {
    let misses: number[] = []
    const at = (seconds: number) => {
      const noted = noteMiss(misses, seconds * 1000, REGULAR_RULE)
      misses = noted.misses
      return noted.struggling
    }

    expect(at(0)).toBe(false)
    expect(at(5)).toBe(false)
    expect(at(10)).toBe(true)
  })

  it('are forgotten when they are too old to count', () => {
    // Two, then a third long after: the first two no longer count
    const first = noteMiss([], 0, REGULAR_RULE)
    const second = noteMiss(first.misses, 1_000, REGULAR_RULE)
    const third = noteMiss(second.misses, 1_000 + REGULAR_RULE.withinMs + 1, REGULAR_RULE)

    expect(third.struggling).toBe(false)
    expect(third.misses).toEqual([1_000 + REGULAR_RULE.withinMs + 1])
  })

  it('are fewer for somebody who has never picked a time, such as a double click', () => {
    const first = noteMiss([], 0, NEWCOMER_RULE)
    expect(first.struggling).toBe(false)
    expect(noteMiss(first.misses, 250, NEWCOMER_RULE).struggling).toBe(true)
    expect(noteMiss(noteMiss([], 0, REGULAR_RULE).misses, 250, REGULAR_RULE).struggling).toBe(false)
  })
})
