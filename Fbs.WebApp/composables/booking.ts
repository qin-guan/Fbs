import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

export function useBookings() {
  return useQuery({
    queryKey: ['bookings'],
    queryFn: () => $api.booking.get(),
  })
}

export function useBooking(id: MaybeRef<string>) {
  return useQuery({
    queryKey: ['bookings', toValue(id)],
    queryFn: () => $api.booking.byId(toValue(id)).get(),
  })
}

export function useCreateBookingMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: $api.booking.post,
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
    },
  })
}

export function useDeleteBookingMutation(id: MaybeRef<string>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: $api.booking.byId(toValue(id)).delete,
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
    },
  })
}

export function useUpdateBookingMutation(id: MaybeRef<string>) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: $api.booking.byId(toValue(id)).post,
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
    },
  })
}
