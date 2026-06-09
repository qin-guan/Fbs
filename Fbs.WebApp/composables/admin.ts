import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type {
  FbsWebApiDtosBookingWithUser,
  FbsWebApiEndpointsBookingByIdPostRequest,
  FbsWebApiEntitiesBooking,
  FbsWebApiEntitiesUser,
} from '~/api/models'

export interface AdminUser extends FbsWebApiEntitiesUser {
  isAdmin?: boolean | null
}

function createAdminClient() {
  const config = useRuntimeConfig()
  return $fetch.create({
    baseURL: config.public.api,
    credentials: 'include',
  })
}

export function useAdminBookings() {
  const client = createAdminClient()
  return useQuery<FbsWebApiDtosBookingWithUser[]>({
    queryKey: ['admin-bookings'],
    queryFn: () => client<FbsWebApiDtosBookingWithUser[]>('/Admin/Bookings', {
      headers: {
        'Content-Type': 'application/json',
      },
    }),
  })
}

export function useAdminBooking(id: MaybeRef<string>) {
  const bookings = useAdminBookings()

  return {
    ...bookings,
    data: computed(() => bookings.data.value?.find(booking => booking.id === toValue(id))),
  }
}

export function useAdminDeleteBookingMutation() {
  const queryClient = useQueryClient()
  const client = createAdminClient()

  return useMutation({
    mutationFn: (bookingId: string) =>
      client<void>(`/Admin/Bookings/${bookingId}`, {
        method: 'DELETE',
        headers: {
          'Content-Type': 'application/json',
        },
      }),
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['admin-bookings'] })
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
    },
  })
}

export function useAdminUpdateBookingMutation() {
  const queryClient = useQueryClient()
  const client = createAdminClient()

  return useMutation({
    mutationFn: ({ bookingId, data }: { bookingId: string; data: FbsWebApiEndpointsBookingByIdPostRequest }) =>
      client<FbsWebApiEntitiesBooking>(`/Admin/Bookings/${bookingId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: data,
      }),
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['admin-bookings'] })
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
    },
  })
}

export function useAdminUsers() {
  const client = createAdminClient()
  return useQuery<AdminUser[]>({
    queryKey: ['admin-users'],
    queryFn: () => client<AdminUser[]>('/Admin/Users', {
      headers: {
        'Content-Type': 'application/json',
      },
    }),
  })
}

export function useToggleAdminMutation() {
  const queryClient = useQueryClient()
  const client = createAdminClient()

  return useMutation({
    mutationFn: (phone: string) =>
      client<AdminUser>(`/Admin/Users/${phone}/Admin`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
      }),
    onSuccess() {
      queryClient.invalidateQueries({ queryKey: ['admin-users'] })
    },
  })
}
