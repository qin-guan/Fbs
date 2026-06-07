import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

const client = $fetch.create({
  baseURL: 'https://3sib-fbs-api.from.sg',
  credentials: 'include'
})

export function useAdminBookings() {
  return useQuery({
    queryKey: ['admin-bookings'],
    queryFn: () => client('/Admin/Bookings', {
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
      client(`/Admin/Bookings/${bookingId}`, {
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
      client(`/Admin/Bookings/${bookingId}`, {
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
    queryFn: () => client('/Admin/Users', {
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
      client(`/Admin/Users/${phone}/Admin`, {
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
