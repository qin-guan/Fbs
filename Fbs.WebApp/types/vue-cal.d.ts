/* eslint-disable @typescript-eslint/no-explicit-any */

declare module 'vue-cal' {
  export function addDatePrototypes(): void

  export function addDays(s: any, _: any): any

  export function addHours(s: any, _: any): any

  export function addMinutes(s: any, _: any): any

  export function countDays(s: any, _: any): any

  export function dateToMinutes(s: any): void

  export function datesInSameTimeStep(s: any, _: any, h: any): void

  export function formatDate(s: any, _: any, h: any): any

  export function formatDateLite(s: any): any

  export function formatMinutes(s: any): any

  export function formatTime(s: any, _: any, h: any, F: any): any

  export function formatTimeLite(s: any): any

  export function getPreviousFirstDayOfWeek(s: any, _: any): any

  export function getWeek(s: any, _: any): any

  export function isInRange(s: any, _: any, h: any): void

  export function isLeapYear(s: any): any

  export function isSameDate(s: any, _: any): any

  export function isToday(s: any): void

  export function isValidDate(s: any): void

  export function removeDatePrototypes(): void

  export function stringToDate(s: any): void

  export function subtractDays(s: any, _: any): any

  export function subtractHours(s: any, _: any): any

  export function subtractMinutes(s: any, _: any): any

  export function updateTexts(s: any): void

  export function useLocale(p: any): void

  export namespace VueCal {
    const emits: string[]

    const props: {
      allDayEvents: {
        default: boolean
        type: any[]
      }
      clickToNavigate: {
        default: any
        type: any
      }
      dark: {
        default: boolean
        type: any
      }
      datePicker: {
        default: boolean
        type: any
      }
      disableDays: {
        default: any
        type: any
      }
      editableEvents: {
        default: boolean
        type: any[]
      }
      eventCount: {
        default: boolean
        type: any[]
      }
      eventCreateMinDrag: {
        default: number
        type: any
      }
      events: {
        default: any
        type: any
      }
      eventsOnMonthView: {
        default: boolean
        type: any
      }
      hideWeekdays: {
        default: any
        type: any
      }
      hideWeekends: {
        default: boolean
        type: any
      }
      locale: {
        default: string
        type: any
      }
      maxDate: {
        default: string
        type: any[]
      }
      minDate: {
        default: string
        type: any[]
      }
      schedules: {
        default: any
        type: any
      }
      selectedDate: {
        default: string
        type: any[]
      }
      sm: {
        default: boolean
        type: any
      }
      snapToInterval: {
        default: number
        type: any
      }
      specialHours: {
        default: any
        type: any
      }
      stackEvents: {
        default: boolean
        type: any
      }
      startWeekOnSunday: {
        default: boolean
        type: any
      }
      theme: {
        default: string
        type: any[]
      }
      time: {
        default: boolean
        type: any
      }
      timeAtCursor: {
        default: boolean
        type: any
      }
      timeCellHeight: {
        default: number
        type: any
      }
      timeFormat: {
        default: string
        type: any
      }
      timeFrom: {
        default: number
        type: any
      }
      timeStep: {
        default: number
        type: any
      }
      timeTo: {
        default: number
        type: any
      }
      titleBar: {
        default: boolean
        type: any
      }
      todayButton: {
        default: boolean
        type: any
      }
      twelveHour: {
        default: boolean
        type: any
      }
      view: {
        default: string
        type: any
      }
      viewDate: {
        default: string
        type: any[]
      }
      viewDayOffset: {
        default: number
        type: any
      }
      views: {
        type: any[]
      }
      viewsBar: {
        default: boolean
        type: any
      }
      watchRealTime: {
        default: boolean
        type: any
      }
      weekNumbers: {
        default: boolean
        type: any
      }
      xs: {
        default: boolean
        type: any
      }
    }

    function setup(...args: any[]): void

  }
}
