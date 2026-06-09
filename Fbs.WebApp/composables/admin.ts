import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type {
  FbsWebApiDtosBookingWithUser,
  FbsWebApiEntitiesBooking,
  FbsWebApiEntitiesUser,
} from '~/api/models'

export interface AdminUser extends FbsWebApiEntitiesUser {
  isAdmin?: boolean | null
}

export interface AdminBookingUpdateRequest {
  id: string
  conduct?: string | null
  description?: string | null
  pocName?: string | null
  pocPhone?: string | null
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
  const client = createAdminClient()
  const queryClient = useQueryClient()

  return useQuery<FbsWebApiDtosBookingWithUser>({
    queryKey: computed(() => ['admin-bookings', toValue(id)]),
    queryFn: () => client<FbsWebApiDtosBookingWithUser>(`/Admin/Bookings/${toValue(id)}`, {
      headers: {
        'Content-Type': 'application/json',
      },
    }),
    initialData: () => {
      return queryClient.getQueryData<FbsWebApiDtosBookingWithUser[]>(['admin-bookings'])
        ?.find(booking => booking.id === toValue(id))
    },
  })
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
    onSuccess(_, bookingId) {
      queryClient.invalidateQueries({ queryKey: ['admin-bookings'] })
      queryClient.invalidateQueries({ queryKey: ['admin-bookings', bookingId] })
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
      queryClient.invalidateQueries({ queryKey: ['bookings', bookingId] })
    },
  })
}

export function useAdminUpdateBookingMutation() {
  const queryClient = useQueryClient()
  const client = createAdminClient()

  return useMutation({
    mutationFn: ({ bookingId, data }: { bookingId: string; data: AdminBookingUpdateRequest }) =>
      client<FbsWebApiEntitiesBooking>(`/Admin/Bookings/${bookingId}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: data,
      }),
    onSuccess(_, variables) {
      queryClient.invalidateQueries({ queryKey: ['admin-bookings'] })
      queryClient.invalidateQueries({ queryKey: ['admin-bookings', variables.bookingId] })
      queryClient.invalidateQueries({ queryKey: ['bookings'] })
      queryClient.invalidateQueries({ queryKey: ['bookings', variables.bookingId] })
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
