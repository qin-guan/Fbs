import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

export function useAdminBookings() {
  return useQuery({
    queryKey: ['admin-bookings'],
    queryFn: () => $fetch('/Admin/Bookings', {
      headers: {
        'Content-Type': 'application/json'
      }
    }),
  })
}

export function useAdminDeleteBookingMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (bookingId: string) =>
      $fetch(`/Admin/Bookings/${bookingId}`, {
        method: 'DELETE',
        headers: {
          'Content-Type': 'application/json'
        }
      }),
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['admin-bookings'] })
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
    },
  })
}

export function useAdminUpdateBookingMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ bookingId, data }: { bookingId: string; data: any }) =>
      $fetch(`/Admin/Bookings/${bookingId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json'
        },
        body: data
      }),
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['admin-bookings'] })
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
    },
  })
}

export function useAdminUsers() {
  return useQuery({
    queryKey: ['admin-users'],
    queryFn: () => $fetch('/Admin/Users', {
      headers: {
        'Content-Type': 'application/json'
      }
    }),
  })
}

export function useToggleAdminMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (phone: string) =>
      $fetch(`/Admin/Users/${phone}/Admin`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        }
      }),
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['admin-users'] })
    },
  })
}
