<script setup lang="ts">
import type { FormError } from '@nuxt/ui'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import type { OrgCalendar } from '~/composables/orgCalendar'

const queryClient = useQueryClient()
const toast = useToast()
const { slug, isAdmin } = useTenant()
const { dtf } = useTenantFormatter()

const queryKey = computed(() => ['org-calendar', slug.value] as const)
const { data: calendar, isPending, error } = useQuery({
  queryKey,
  queryFn: () => getOrgCalendar(slug.value),
  enabled: isAdmin,
})

const replacing = ref(false)
watch(() => calendar.value?.status, (status) => {
  if (status !== 'Pending') {
    replacing.value = false
  }
})

const connect = reactive({ calendarId: '' })
const confirm = reactive({ code: '' })
const connectForm = useTemplateRef('connectForm')
const confirmForm = useTemplateRef('confirmForm')
const starting = ref(false)
const confirming = ref(false)
const stopping = ref(false)

const showCode = computed(() => calendar.value?.status === 'Pending' && !replacing.value)
const showConnect = computed(() => !!calendar.value && !showCode.value && calendar.value.status !== 'Active')
const connected = computed(() => calendar.value?.status === 'Active')

function remember(next: OrgCalendar) {
  queryClient.setQueryData(queryKey.value, next)
  connect.calendarId = ''
  confirm.code = ''
  replacing.value = false
}

function complain(title: string, e: unknown, setErrors?: (errors: FormError[]) => void) {
  const fields = getFieldErrors(e)
  if (fields.length && setErrors) {
    setErrors(fields)
  }
  else {
    toast.add({ title, description: getErrorReasons(e)[0] ?? 'Try again in a moment.', color: 'error' })
  }
}

function validateCalendar(values: typeof connect): FormError[] {
  const id = values.calendarId.trim()
  if (id.length < 1 || id.length > 256 || /[\s/?#]/.test(id) || id.toLowerCase() === 'primary') {
    return [{ name: 'calendarId', message: 'The calendar id is the address of the calendar, up to 256 characters, and not primary.' }]
  }

  return []
}

function validateCode(values: typeof confirm): FormError[] {
  if (values.code.trim().length < 1) {
    return [{ name: 'code', message: 'Type the code from the calendar.' }]
  }

  return []
}

function until(value: OrgCalendar['verificationExpiresAt']) {
  if (!value) {
    return ''
  }

  const date = value instanceof Date ? value : new Date(value)
  return dtf.value.format(date)
}

async function start() {
  starting.value = true
  try {
    remember(await startOrgCalendar(slug.value, connect.calendarId.trim()))
    toast.add({ title: 'Look on the calendar for the code', color: 'success' })
  }
  catch (e) {
    complain('Couldn\'t connect the calendar', e, errors => connectForm.value?.setErrors(errors))
  }
  finally {
    starting.value = false
  }
}

async function submitCode() {
  confirming.value = true
  try {
    remember(await confirmOrgCalendar(slug.value, confirm.code.trim()))
    toast.add({ title: 'The calendar is connected', description: 'Bookings are copied to it.', color: 'success' })
  }
  catch (e) {
    complain('That code wasn\'t accepted', e, errors => confirmForm.value?.setErrors(errors))
  }
  finally {
    confirming.value = false
  }
}

async function stop() {
  stopping.value = true
  try {
    await deleteOrgCalendar(slug.value)
    remember({
      status: 'None',
      calendarId: null,
      lastError: null,
      verificationExpiresAt: null,
      serviceAccountEmail: calendar.value?.serviceAccountEmail ?? null,
    })
    toast.add({ title: 'Stopped copying to the calendar', color: 'success' })
  }
  catch (e) {
    toast.add({ title: 'Couldn\'t stop copying', description: getErrorReasons(e)[0] ?? 'Try again in a moment.', color: 'error' })
  }
  finally {
    stopping.value = false
  }
}
</script>

<template>
  <UPageCard
    title="Google Calendar"
    description="Bookings are copied to a Google Calendar. What is changed there is not read back."
    variant="subtle"
    icon="i-lucide-calendar"
  >
    <UAlert
      v-if="error"
      title="Couldn't load the calendar"
      :description="getErrorReasons(error)[0] ?? 'Try again in a moment.'"
      color="error"
      variant="subtle"
      icon="i-lucide-circle-alert"
    />

    <div
      v-else-if="isPending"
      class="flex items-center gap-2 text-sm text-muted"
    >
      <UIcon
        name="i-lucide-loader-circle"
        class="size-4 animate-spin"
      />
      Loading the calendar
    </div>

    <div
      v-else-if="calendar"
      class="space-y-4"
    >
      <UAlert
        v-if="calendar.status === 'Failed'"
        title="Copying has stopped"
        :description="calendar.lastError || 'Google Calendar refused it. Connect it again once it is shared.'"
        color="error"
        variant="subtle"
        icon="i-lucide-circle-alert"
      />

      <div
        v-if="connected"
        class="space-y-3"
      >
        <p class="text-sm">
          Bookings are copied to <span class="font-medium">{{ calendar.calendarId }}</span>.
        </p>
        <UButton
          label="Stop copying"
          icon="i-lucide-calendar-x"
          color="neutral"
          variant="subtle"
          :loading="stopping"
          @click="stop"
        />
        <p class="text-sm text-muted">
          Events already on the calendar stay there.
        </p>
      </div>

      <UForm
        v-else-if="showCode"
        ref="confirmForm"
        :state="confirm"
        :validate="validateCode"
        class="space-y-4"
        @submit="submitCode"
      >
        <p class="text-sm">
          An event whose name starts with Fbs was added to <span class="font-medium">{{ calendar.calendarId }}</span>.
          Open that calendar, read the code in the event's name, and type it here.
          <template v-if="until(calendar.verificationExpiresAt)">
            It works until {{ until(calendar.verificationExpiresAt) }}.
          </template>
        </p>
        <UFormField
          label="Code"
          name="code"
          required
        >
          <UInput
            v-model="confirm.code"
            class="w-full sm:w-56"
            autocomplete="off"
            spellcheck="false"
          />
        </UFormField>
        <div class="flex flex-wrap gap-2">
          <UButton
            type="submit"
            label="Confirm the code"
            icon="i-lucide-check"
            :loading="confirming"
          />
          <UButton
            type="button"
            label="Use a different calendar"
            color="neutral"
            variant="ghost"
            :disabled="confirming"
            @click="replacing = true"
          />
          <UButton
            type="button"
            label="Cancel"
            color="neutral"
            variant="ghost"
            :loading="stopping"
            :disabled="confirming"
            @click="stop"
          />
        </div>
      </UForm>

      <UForm
        v-else-if="showConnect"
        ref="connectForm"
        :state="connect"
        :validate="validateCalendar"
        class="space-y-4"
        @submit="start"
      >
        <p
          v-if="calendar.serviceAccountEmail"
          class="text-sm text-muted"
        >
          Share the calendar with <span class="text-default">{{ calendar.serviceAccountEmail }}</span>, and let that account change events.
        </p>
        <p
          v-else
          class="text-sm text-muted"
        >
          Share the calendar with the service account this app uses, and let that account change events.
        </p>
        <UFormField
          label="Calendar id"
          name="calendarId"
          description="In Google Calendar, open the calendar's settings and copy Calendar ID. It often ends in @group.calendar.google.com."
          required
        >
          <UInput
            v-model="connect.calendarId"
            class="w-full"
            autocomplete="off"
            spellcheck="false"
          />
        </UFormField>
        <div class="flex flex-wrap gap-2">
          <UButton
            type="submit"
            label="Connect the calendar"
            icon="i-lucide-calendar-plus"
            :loading="starting"
          />
          <UButton
            v-if="calendar.status === 'Pending'"
            type="button"
            label="Back to the code"
            color="neutral"
            variant="ghost"
            :disabled="starting"
            @click="replacing = false"
          />
          <UButton
            v-if="calendar.status === 'Failed' || calendar.status === 'Disabled'"
            type="button"
            label="Stop copying"
            color="neutral"
            variant="ghost"
            :loading="stopping"
            :disabled="starting"
            @click="stop"
          />
        </div>
      </UForm>
    </div>
  </UPageCard>
</template>