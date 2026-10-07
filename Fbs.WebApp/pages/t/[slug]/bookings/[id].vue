<script setup lang="ts">
import { useGetOrgBookingsById, useDeleteOrgBookingsById, usePutOrgBookingsById } from '~/api'

definePageMeta({
  layout: 'tenant',
})

useHead({ title: 'Booking' })

const route = useRoute()
const router = useRouter()
const toast = useToast()
const invalidateBookings = useInvalidateBookings()
const { slug, org, timeZone, path } = useTenant()
const { df, tf } = useTenantFormatter()

const id = computed(() => String(route.params.id))
const { data: booking, isPending, error } = useGetOrgBookingsById(
  { path: computed(() => ({ slug: slug.value, id: id.value })) },
  { query: { retry: false } },
)

const now = useNow({ interval: 30_000 })
const isOver = computed(() => !!booking.value && booking.value.endDateTime <= now.value)
const hasStarted = computed(() => !!booking.value && booking.value.startDateTime <= now.value)

const editing = ref(false)
const form = reactive({ conduct: '', description: '', pocName: '', pocPhone: '', start: '', end: '' })
const problems = ref<string[]>([])

function startEditing() {
  const b = booking.value
  if (!b) {
    return
  }

  Object.assign(form, {
    conduct: b.conduct,
    description: b.description ?? '',
    pocName: b.pocName ?? '',
    pocPhone: b.pocPhone ?? '',
    start: toLocalInput(b.startDateTime, timeZone.value),
    end: toLocalInput(b.endDateTime, timeZone.value),
  })
  problems.value = []
  editing.value = true
}

const { mutateAsync: save, isPending: saving } = usePutOrgBookingsById()

async function submit() {
  const b = booking.value
  if (!b) {
    return
  }

  problems.value = []
  const start = fromLocalInput(form.start, timeZone.value)
  const end = fromLocalInput(form.end, timeZone.value)
  const moved = !!start && !!end && (start.getTime() !== b.startDateTime.getTime() || end.getTime() !== b.endDateTime.getTime())

  try {
    await save({
      path: { slug: slug.value, id: b.id },
      body: {
        conduct: form.conduct,
        description: form.description || null,
        pocName: form.pocName || null,
        pocPhone: form.pocPhone || null,
        startDateTime: moved ? start : null,
        endDateTime: moved ? end : null,
      },
    })
    await invalidateBookings(slug.value)
    editing.value = false
    toast.add({ title: 'Saved', color: 'success', icon: 'i-lucide-circle-check', duration: 3000 })
  }
  catch (e) {
    const reasons = getErrorReasons(e)
    problems.value = reasons.length ? reasons : [getErrorStatus(e) === 403 ? 'You can only change bookings made by you or your unit.' : 'It could not be saved. Try again.']
  }
}

const cancelling = ref(false)

function askToCancel() {
  cancelling.value = true
}

function keep() {
  cancelling.value = false
}

function stopEditing() {
  editing.value = false
}
const { mutateAsync: cancel, isPending: cancelPending } = useDeleteOrgBookingsById()

async function cancelBooking() {
  const b = booking.value
  if (!b) {
    return
  }

  try {
    await cancel({ path: { slug: slug.value, id: b.id } })
    await invalidateBookings(slug.value)
    toast.add({ title: 'Booking cancelled', color: 'success', icon: 'i-lucide-circle-check', duration: 3000 })
    await router.push(path())
  }
  catch {
    toast.add({ title: 'Couldn\'t cancel it', description: 'Somebody may have done that already.', color: 'error' })
    cancelling.value = false
    await invalidateBookings(slug.value)
  }
}

const details = computed(() => {
  const b = booking.value
  if (!b) {
    return []
  }

  return [
    { label: 'Facility', value: b.facilityName },
    { label: 'Date', value: b.startDateTime.toDateString() === b.endDateTime.toDateString() ? df.value.format(b.startDateTime) : `${df.value.format(b.startDateTime)} – ${df.value.format(b.endDateTime)}` },
    { label: 'Time', value: `${tf.value.format(b.startDateTime)} – ${tf.value.format(b.endDateTime)}` },
    { label: 'Conduct', value: b.conduct },
    { label: 'Description', value: b.description },
    { label: 'Point of contact', value: [b.pocName, b.pocPhone].filter(Boolean).join(', ') },
    { label: 'Booked by', value: b.bookedBy.displayName },
    { label: 'Last changed by', value: b.updatedBy?.displayName },
  ].filter(d => d.value)
})
</script>

<template>
  <UDashboardPanel id="booking">
    <template #header>
      <AppNavbar title="Booking">
        <template #right>
          <UButton
            :to="path()"
            icon="i-lucide-arrow-left"
            label="All bookings"
            color="neutral"
            variant="ghost"
          />
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <div class="w-full lg:max-w-2xl mx-auto space-y-4">
        <USkeleton
          v-if="isPending"
          class="h-64 w-full"
        />

        <UAlert
          v-else-if="error || !booking"
          title="This booking isn't there"
          description="It may have been cancelled, or belong to another organization."
          color="neutral"
          variant="subtle"
          icon="i-lucide-circle-slash"
          :actions="[{ label: 'All bookings', to: path(), color: 'neutral', variant: 'subtle' }]"
        />

        <template v-else>
          <UPageCard
            :title="booking.conduct"
            :description="`${booking.facilityName} · ${df.format(booking.startDateTime)}, ${tf.format(booking.startDateTime)} – ${tf.format(booking.endDateTime)}`"
            variant="subtle"
          >
            <template
              v-if="isOver || hasStarted"
              #footer
            >
              <UBadge
                :label="isOver ? 'Over' : 'Underway'"
                :color="isOver ? 'neutral' : 'success'"
                variant="subtle"
              />
            </template>
          </UPageCard>

          <UPageCard
            v-if="!editing"
            variant="subtle"
          >
            <dl class="space-y-3">
              <div
                v-for="detail in details"
                :key="detail.label"
                class="flex max-sm:flex-col gap-1 sm:gap-4"
              >
                <dt class="w-40 shrink-0 text-sm text-muted">
                  {{ detail.label }}
                </dt>
                <dd class="text-highlighted">
                  {{ detail.value }}
                </dd>
              </div>
            </dl>

            <div
              v-if="booking.canManage"
              class="flex flex-wrap gap-2 pt-2"
            >
              <UButton
                label="Edit"
                icon="i-lucide-pencil"
                @click="startEditing"
              />
              <UButton
                label="Cancel booking"
                icon="i-lucide-calendar-x"
                color="error"
                variant="subtle"
                @click="askToCancel"
              />
            </div>
            <p
              v-else
              class="text-sm text-muted"
            >
              Only {{ booking.bookedBy.displayName }}, their unit and admins can change this.
            </p>
          </UPageCard>

          <UPageCard
            v-else
            title="Edit booking"
            variant="subtle"
          >
            <form
              class="space-y-4"
              @submit.prevent="submit"
            >
              <UFormField
                label="Conduct"
                required
              >
                <UInput
                  v-model="form.conduct"
                  class="w-full"
                  maxlength="100"
                  required
                />
              </UFormField>

              <UFormField label="Description">
                <UTextarea
                  v-model="form.description"
                  class="w-full"
                  maxlength="2000"
                />
              </UFormField>

              <div class="grid gap-4 sm:grid-cols-2">
                <UFormField label="Point of contact">
                  <UInput
                    v-model="form.pocName"
                    class="w-full"
                    maxlength="200"
                  />
                </UFormField>
                <UFormField label="Their phone">
                  <UInput
                    v-model="form.pocPhone"
                    class="w-full"
                    maxlength="32"
                    inputmode="tel"
                  />
                </UFormField>
              </div>

              <div class="grid gap-4 sm:grid-cols-2">
                <UFormField
                  label="Starts"
                  :description="isOver ? 'This booking is over, so the time can\'t be changed.' : hasStarted ? 'This booking has started. You can only change when it ends.' : undefined"
                >
                  <UInput
                    v-model="form.start"
                    type="datetime-local"
                    class="w-full"
                    :step="(org?.slotMinutes ?? 30) * 60"
                    :disabled="isOver || hasStarted"
                    aria-label="Starts"
                  />
                </UFormField>
                <UFormField label="Ends">
                  <UInput
                    v-model="form.end"
                    type="datetime-local"
                    class="w-full"
                    :step="(org?.slotMinutes ?? 30) * 60"
                    :disabled="isOver"
                    aria-label="Ends"
                  />
                </UFormField>
              </div>

              <UAlert
                v-if="problems.length"
                title="It couldn't be saved"
                color="error"
                variant="subtle"
                icon="i-lucide-circle-alert"
              >
                <template #description>
                  <ul class="list-disc pl-4">
                    <li
                      v-for="problem in problems"
                      :key="problem"
                    >
                      {{ problem }}
                    </li>
                  </ul>
                </template>
              </UAlert>

              <div class="flex gap-2">
                <UButton
                  type="submit"
                  label="Save"
                  :loading="saving"
                />
                <UButton
                  type="button"
                  label="Cancel"
                  color="neutral"
                  variant="ghost"
                  @click="stopEditing"
                />
              </div>
            </form>
          </UPageCard>
        </template>
      </div>

      <UModal
        v-model:open="cancelling"
        title="Cancel this booking?"
        description="The record stays and the time opens up. Anyone already notified is told it's cancelled."
      >
        <template #footer>
          <div class="flex gap-2">
            <UButton
              label="Cancel booking"
              color="error"
              :loading="cancelPending"
              @click="cancelBooking"
            />
            <UButton
              label="Keep it"
              color="neutral"
              variant="ghost"
              @click="keep"
            />
          </div>
        </template>
      </UModal>
    </template>
  </UDashboardPanel>
</template>
