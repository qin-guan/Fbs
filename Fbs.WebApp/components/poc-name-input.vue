<script setup lang="ts">
// Free-text input for a POC's rank and name, with suggestions from the nominal roll.
// Picking a suggestion emits `select` with the person's phone number so callers can fill it in.
const model = defineModel<string | null | undefined>()

const props = defineProps<{
  id?: string
  placeholder?: string
  disabled?: boolean
}>()

const emit = defineEmits<{
  select: [option: { name: string, phone: string }]
}>()

const { data: nominalRoll, isPending: nominalRollIsPending } = useNominalRollMapping()
const nominalRollMiniSearch = useNominalRollMiniSearch()

const open = ref(false)
const highlighted = ref(-1)
const anchor = useTemplateRef<HTMLElement>('anchor')
const listId = useId()

const suggestions = computed(() => {
  if (!nominalRoll.value) return []

  const query = model.value?.trim()
  const phones = query
    ? nominalRollMiniSearch.value?.search(query, { prefix: true }).map(e => e.id as string) ?? []
    : Object.keys(nominalRoll.value)

  return phones
    .map(phone => ({ phone, name: nominalRoll.value?.[phone] ?? '' }))
    .filter(option => option.name)
})

watch(suggestions, () => {
  highlighted.value = -1
})

function onInput(value: string | number | null | undefined) {
  open.value = !!String(value ?? '').trim()
}

function toggle() {
  open.value = !open.value
  anchor.value?.querySelector('input')?.focus()
}

function select(option: { name: string, phone: string }) {
  model.value = option.name
  emit('select', option)
  open.value = false
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault()
    if (!open.value) {
      open.value = true
      return
    }
    const count = suggestions.value.length
    if (!count) return
    const step = e.key === 'ArrowDown' ? 1 : -1
    highlighted.value = (highlighted.value + step + count) % count
    document.getElementById(`${listId}-${highlighted.value}`)?.scrollIntoView({ block: 'nearest' })
  }
  else if (e.key === 'Enter' && open.value && suggestions.value[highlighted.value]) {
    e.preventDefault()
    select(suggestions.value[highlighted.value]!)
  }
  else if (e.key === 'Escape' && open.value) {
    e.preventDefault()
    open.value = false
  }
}

function onInteractOutside(e: Event) {
  if (anchor.value?.contains(e.target as Node)) {
    e.preventDefault()
  }
}
</script>

<template>
  <UPopover
    v-model:open="open"
    :content="{
      align: 'start',
      sideOffset: 4,
      onOpenAutoFocus: (e: Event) => e.preventDefault(),
      onCloseAutoFocus: (e: Event) => e.preventDefault(),
      onInteractOutside,
    }"
    :ui="{ content: 'w-(--reka-popper-anchor-width) max-h-72 overflow-y-auto p-1' }"
  >
    <template #anchor>
      <div
        ref="anchor"
        class="w-full"
      >
        <UInput
          :id="props.id"
          v-model="model"
          :placeholder="placeholder"
          :disabled="disabled"
          :loading="nominalRollIsPending"
          autocomplete="off"
          role="combobox"
          aria-autocomplete="list"
          :aria-expanded="open"
          :aria-controls="listId"
          :aria-activedescendant="highlighted >= 0 ? `${listId}-${highlighted}` : undefined"
          class="w-full"
          :ui="{ trailing: 'pe-1' }"
          @update:model-value="onInput"
          @keydown="onKeydown"
        >
          <template #trailing>
            <UButton
              color="neutral"
              variant="link"
              size="sm"
              icon="i-lucide-chevron-down"
              aria-label="Show suggestions"
              tabindex="-1"
              :disabled="disabled"
              @click="toggle"
            />
          </template>
        </UInput>
      </div>
    </template>

    <template #content>
      <ul
        :id="listId"
        role="listbox"
      >
        <li
          v-for="(option, index) in suggestions"
          :id="`${listId}-${index}`"
          :key="option.phone"
          role="option"
          :aria-selected="index === highlighted"
          class="flex cursor-pointer items-center justify-between gap-3 rounded-md px-2 py-1.5 text-sm"
          :class="index === highlighted ? 'bg-elevated text-highlighted' : 'text-default hover:bg-elevated/50'"
          @pointerdown.prevent
          @click="select(option)"
          @pointermove="highlighted = index"
        >
          <span class="truncate">{{ option.name }}</span>
          <span class="shrink-0 text-xs text-dimmed">{{ option.phone }}</span>
        </li>
        <li
          v-if="!suggestions.length"
          class="px-2 py-1.5 text-sm text-muted"
        >
          No matches in the nominal roll. You can still enter a name.
        </li>
      </ul>
    </template>
  </UPopover>
</template>
