import { useQueryClient } from '@tanstack/vue-query'
import {
  getBookingByIdQueryKey,
  getBookingQueryKey,
  useDeleteBookingById,
  usePostBookingBatch,
  usePostBookingById,
} from '~/api'

export function useCreateBookingBatchMutation() {
  const queryClient = useQueryClient()

  return usePostBookingBatch({
    mutation: {
      // A rejected batch usually means someone else booked first, so refresh either way
      onSettled() {
        queryClient.invalidateQueries({ queryKey: getBookingQueryKey() })
      },
    },
  })
}

export function useDeleteBookingMutation() {
  const queryClient = useQueryClient()

  return useDeleteBookingById({
    mutation: {
      onSuccess(_, { path }) {
        queryClient.invalidateQueries({ queryKey: getBookingQueryKey() })
        queryClient.invalidateQueries({ queryKey: getBookingByIdQueryKey({ path }) })
      },
    },
  })
}

export function useUpdateBookingMutation() {
  const queryClient = useQueryClient()

  return usePostBookingById({
    mutation: {
      onSuccess(_, { path }) {
        queryClient.invalidateQueries({ queryKey: getBookingQueryKey() })
        queryClient.invalidateQueries({ queryKey: getBookingByIdQueryKey({ path }) })
      },
    },
  })
}
