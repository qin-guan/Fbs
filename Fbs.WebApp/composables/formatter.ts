import { DateFormatter } from '@internationalized/date'

export const useFormatter = createSharedComposable(() => {
  return {
    df: new DateFormatter('en-SG', {
      dateStyle: 'medium',
    }),

    tf: new DateFormatter('en-SG', {
      timeStyle: 'short',
    }),
  }
})
