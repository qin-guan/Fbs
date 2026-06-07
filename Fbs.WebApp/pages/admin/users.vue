<script setup lang="ts">
import type { FbsWebApiEntitiesUser } from '~/api/models'

definePageMeta({
  layout: 'app',
})

const toast = useToast()

const { data: users, isPending: usersIsPending } = useAdminUsers()
const { mutate: toggleAdmin, isPending: toggleIsPending } = useToggleAdminMutation()

const selectedUser = ref<FbsWebApiEntitiesUser | null>(null)
const confirmDialog = ref(false)

const defaultFilters = {
  global: { value: null, matchMode: 'contains' },
  name: { value: null, matchMode: 'contains' },
  phone: { value: null, matchMode: 'contains' },
  unit: { value: null, matchMode: 'contains' },
}

const filters = ref(defaultFilters)

function clearFilters() {
  filters.value = defaultFilters
}

function onToggleAdmin(user: FbsWebApiEntitiesUser) {
  selectedUser.value = user
  confirmDialog.value = true
}

function confirmToggle() {
  if (!selectedUser.value?.phone) return
  
  toggleAdmin(selectedUser.value.phone, {
    async onSuccess() {
      confirmDialog.value = false
      const message = selectedUser.value?.isAdmin 
        ? 'User promoted to admin' 
        : 'Admin privileges revoked'
      toast.add({
        severity: 'success',
        summary: message,
        life: 3000,
      })
      selectedUser.value = null
    },
    onError() {
      toast.add({
        severity: 'error',
        summary: 'Failed to toggle admin status.',
        life: 3000,
      })
    }
  })
}
</script>

<template>
  <div class="h-full flex flex-col">
    <Dialog
        v-model:visible="confirmDialog"
        modal
        header="Confirm Action"
        :style="{ width: '25rem' }"
    >
      <div class="mb-8 gap-3 flex flex-col">
        <span class="text-surface-500 dark:text-surface-400">
          {{ selectedUser?.isAdmin 
            ? 'Are you sure you want to revoke admin privileges from this user?' 
            : 'Are you sure you want to promote this user to admin?' }}
        </span>
        
        <div v-if="selectedUser" class="bg-surface-50 dark:bg-surface-800 p-3 rounded">
          <p class="text-sm"><strong>Name:</strong> {{ selectedUser.name }}</p>
          <p class="text-sm"><strong>Phone:</strong> {{ selectedUser.phone }}</p>
          <p class="text-sm"><strong>Unit:</strong> {{ selectedUser.unit }}</p>
          <p class="text-sm"><strong>Current Status:</strong> 
            <Tag :value="selectedUser.isAdmin ? 'Admin' : 'User'" :severity="selectedUser.isAdmin ? 'danger' : 'info'" />
          </p>
        </div>

        <Message :severity="selectedUser?.isAdmin ? 'warn' : 'info'">
          {{ selectedUser?.isAdmin 
            ? 'This user will lose access to admin features.' 
            : 'This user will gain access to the admin panel.' }}
        </Message>
      </div>

      <div class="flex justify-end gap-2">
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
            :loading="toggleIsPending"
            @click="confirmToggle"
        />
      </div>
    </Dialog>

    <AppNavbar>
      <template #content>
        <div class="flex justify-between items-center mr-3">
          <div class="space-x-3 flex items-center">
            <h2>Admin - Manage Users</h2>
            <Button
              type="button"
              label="Clear"
              size="small"
              variant="text"
              @click="clearFilters"
            >
              <template #icon>
                <Icon name="i-lucide-funnel-x" />
              </template>
            </Button>
          </div>
          <div class="flex gap-2">
            <Button
              v-slot="slotProps"
              size="small"
              variant="text"
              as-child
            >
              <NuxtLink
                to="/admin/bookings"
                :class="slotProps.class"
              >
                All Bookings
              </NuxtLink>
            </Button>
          </div>
        </div>
      </template>
    </AppNavbar>

    <div class="flex-1 overflow-hidden p-4">
      <DataTable
          v-if="users"
          :value="users"
          paginator
          :rows="20"
          :global-filter-fields="['name', 'phone', 'unit']"
          :filters="filters"
          filter-display="menu"
          :loading="usersIsPending"
          class="w-full"
      >
        <template #empty>
          <div class="text-center py-8">
            <p class="text-surface-500">No users found.</p>
          </div>
        </template>

        <Column field="name" header="Name" sortable>
          <template #filter="{ filterModel }">
            <InputText v-model="filterModel.value" type="text" placeholder="Search name" />
          </template>
        </Column>

        <Column field="phone" header="Phone" sortable>
          <template #body="{ data }">
            <a :href="`https://api.whatsapp.com/send?phone=${data.phone}`" target="_blank" class="text-blue-500 hover:underline">
              {{ data.phone }}
            </a>
          </template>
          <template #filter="{ filterModel }">
            <InputText v-model="filterModel.value" type="text" placeholder="Search phone" />
          </template>
        </Column>

        <Column field="unit" header="Unit" sortable>
          <template #filter="{ filterModel }">
            <InputText v-model="filterModel.value" type="text" placeholder="Search unit" />
          </template>
        </Column>

        <Column header="Status" width="150">
          <template #body="{ data }">
            <Tag 
              :value="data.isAdmin ? 'Admin' : 'User'" 
              :severity="data.isAdmin ? 'danger' : 'info'" 
            />
          </template>
        </Column>

        <Column header="Actions" :frozen="true" align-frozen="right" style="width: 8rem">
          <template #body="{ data }">
            <Button
              :label="data.isAdmin ? 'Revoke' : 'Promote'"
              :severity="data.isAdmin ? 'warn' : 'success'"
              size="small"
              @click="onToggleAdmin(data)"
            />
          </template>
        </Column>
      </DataTable>
    </div>
  </div>
</template>
