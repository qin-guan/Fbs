<script setup lang="ts">
import { FilterMatchMode } from '@primevue/core/api'
import type { AdminUser } from '~/composables/admin'

definePageMeta({
  layout: 'app',
})

const toast = useToast()

const { data: users, isPending: usersIsPending, error: usersError } = useAdminUsers()
const { mutate: toggleAdmin, isPending: toggleIsPending } = useToggleAdminMutation()

const selectedUser = ref<AdminUser | null>(null)
const confirmDialog = ref(false)

function createDefaultFilters() {
  return {
    global: { value: null, matchMode: FilterMatchMode.CONTAINS },
    name: { value: null, matchMode: FilterMatchMode.CONTAINS },
    phone: { value: null, matchMode: FilterMatchMode.CONTAINS },
    unit: { value: null, matchMode: FilterMatchMode.CONTAINS },
  }
}

const filters = ref(createDefaultFilters())

function clearFilters() {
  filters.value = createDefaultFilters()
}

function onToggleAdmin(user: AdminUser) {
  selectedUser.value = user
  confirmDialog.value = true
}

function confirmToggle() {
  if (!selectedUser.value?.phone) return

  const wasAdmin = selectedUser.value.isAdmin === true

  toggleAdmin(selectedUser.value.phone, {
    onSuccess() {
      confirmDialog.value = false
      selectedUser.value = null
      toast.add({
        severity: 'success',
        summary: wasAdmin ? 'Admin privileges revoked.' : 'User promoted to admin.',
        life: 3000,
      })
    },
    onError() {
      toast.add({
        severity: 'error',
        summary: 'Failed to update admin status.',
        detail: 'Please check your admin permissions and try again.',
        life: 4000,
      })
    },
  })
}
</script>

<template>
  <div class="h-full flex flex-col">
    <Dialog
      v-model:visible="confirmDialog"
      modal
      :header="selectedUser?.isAdmin ? 'Revoke admin access' : 'Promote user'"
      :style="{ width: '28rem' }"
    >
      <div class="mb-6 flex flex-col gap-3">
        <p class="text-surface-500">
          {{ selectedUser?.isAdmin
            ? 'This user will lose access to admin features.'
            : 'This user will gain access to the admin interface.' }}
        </p>

        <div
          v-if="selectedUser"
          class="rounded border border-surface-200 bg-surface-50 p-3"
        >
          <p class="text-sm font-semibold">
            {{ selectedUser.name || '-' }}
          </p>
          <p class="text-sm text-surface-500">
            {{ selectedUser.phone || '-' }}
          </p>
          <p class="text-sm text-surface-500">
            {{ selectedUser.unit || '-' }}
          </p>
          <Tag
            class="mt-2"
            :value="selectedUser.isAdmin ? 'Admin' : 'User'"
            :severity="selectedUser.isAdmin ? 'danger' : 'info'"
          />
        </div>

        <Message :severity="selectedUser?.isAdmin ? 'warn' : 'info'">
          {{ selectedUser?.isAdmin
            ? 'They will keep their normal booking account.'
            : 'They will be able to manage all bookings and admin users.' }}
        </Message>
      </div>

      <template #footer>
        <Button
          type="button"
          label="Cancel"
          severity="secondary"
          size="small"
          @click="confirmDialog = false"
        />
        <Button
          type="button"
          size="small"
          :severity="selectedUser?.isAdmin ? 'warn' : 'success'"
          :label="selectedUser?.isAdmin ? 'Revoke' : 'Promote'"
          :icon="selectedUser?.isAdmin ? 'i-lucide-user-minus' : 'i-lucide-user-plus'"
          :loading="toggleIsPending"
          @click="confirmToggle"
        />
      </template>
    </Dialog>

    <AppNavbar>
      <template #content>
        <div class="flex items-center justify-between gap-3 pr-3">
          <div class="flex min-w-0 items-center gap-3">
            <h2 class="truncate text-base font-semibold">
              Admin - Manage Users
            </h2>
            <Button
              type="button"
              label="Clear"
              size="small"
              severity="secondary"
              variant="text"
              @click="clearFilters"
            >
              <template #icon>
                <Icon name="i-lucide-funnel-x" />
              </template>
            </Button>
          </div>

          <Button
            v-slot="slotProps"
            as-child
            size="small"
            severity="secondary"
            variant="outlined"
          >
            <NuxtLink
              to="/admin/bookings"
              :class="slotProps.class"
            >
              <Icon name="i-lucide-calendar-days" />
              <span>All Bookings</span>
            </NuxtLink>
          </Button>
        </div>
      </template>
    </AppNavbar>

    <main class="flex-1 overflow-hidden p-4">
      <Message
        v-if="usersError"
        severity="error"
        class="mb-3"
      >
        Failed to load users. Please check your admin permissions.
      </Message>

      <DataTable
        v-model:filters="filters"
        :value="users ?? []"
        paginator
        :rows="20"
        :rows-per-page-options="[10, 20, 50, 100]"
        :global-filter-fields="['name', 'phone', 'unit']"
        filter-display="menu"
        :loading="usersIsPending"
        data-key="phone"
        class="w-full"
      >
        <template #header>
          <div class="flex flex-wrap items-center justify-between gap-2">
            <IconField>
              <InputIcon>
                <Icon name="i-lucide-search" />
              </InputIcon>
              <InputText
                v-model="filters.global.value"
                placeholder="Search users"
              />
            </IconField>

            <Tag
              :value="`${users?.length ?? 0} users`"
              severity="secondary"
            />
          </div>
        </template>

        <template #empty>
          <div class="py-8 text-center text-surface-500">
            No users found.
          </div>
        </template>

        <Column
          field="name"
          header="Name"
          sortable
        >
          <template #body="{ data }">
            <span class="font-semibold">{{ data.name || '-' }}</span>
          </template>
          <template #filter="{ filterModel }">
            <InputText
              v-model="filterModel.value"
              placeholder="Search name"
            />
          </template>
        </Column>

        <Column
          field="phone"
          header="Phone"
          sortable
        >
          <template #body="{ data }">
            <a
              v-if="data.phone"
              :href="`https://api.whatsapp.com/send?phone=${data.phone}`"
              target="_blank"
              rel="noopener noreferrer"
              class="text-primary hover:underline"
            >
              {{ data.phone }}
            </a>
            <span v-else>-</span>
          </template>
          <template #filter="{ filterModel }">
            <InputText
              v-model="filterModel.value"
              placeholder="Search phone"
            />
          </template>
        </Column>

        <Column
          field="unit"
          header="Unit"
          sortable
        >
          <template #body="{ data }">
            {{ data.unit || '-' }}
          </template>
          <template #filter="{ filterModel }">
            <InputText
              v-model="filterModel.value"
              placeholder="Search unit"
            />
          </template>
        </Column>

        <Column
          field="isAdmin"
          header="Status"
          sortable
          style="width: 9rem"
        >
          <template #body="{ data }">
            <Tag
              :value="data.isAdmin ? 'Admin' : 'User'"
              :severity="data.isAdmin ? 'danger' : 'info'"
            />
          </template>
        </Column>

        <Column
          header="Actions"
          :frozen="true"
          align-frozen="right"
          style="width: 9rem"
        >
          <template #body="{ data }">
            <Button
              :label="data.isAdmin ? 'Revoke' : 'Promote'"
              :severity="data.isAdmin ? 'warn' : 'success'"
              :icon="data.isAdmin ? 'i-lucide-user-minus' : 'i-lucide-user-plus'"
              size="small"
              @click="onToggleAdmin(data)"
            />
          </template>
        </Column>
      </DataTable>
    </main>
  </div>
</template>
