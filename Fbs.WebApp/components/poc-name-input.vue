<script setup lang="ts">
// Free-text input for a POC's rank and name, with suggestions from the nominal roll
// and from custom POCs the user has used before.
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
const { customPocs } = useCustomPocs()

const open = ref(false)
const highlighted = ref(-1)
const anchor = useTemplateRef<HTMLElement>('anchor')
const listId = useId()

const suggestions = computed(() => {
  const query = model.value?.trim()
  const q = query?.toLowerCase() ?? ''
  const rollPhones = new Set<string>()

  const rollOptions = (() => {
    if (!nominalRoll.value) return []

    const phones = query
      ? nominalRollMiniSearch.value?.search(query, { prefix: true }).map(e => e.id as string) ?? []
      : Object.keys(nominalRoll.value)

    return phones
      .map(phone => ({ phone, name: nominalRoll.value?.[phone] ?? '', saved: false }))
      .filter((option) => {
        if (!option.name) return false
        rollPhones.add(option.phone)
        return true
      })
  })()

  const savedOptions = customPocs.value
    .filter((poc) => {
      if (rollPhones.has(poc.phone)) return false
      if (!q) return true
      return poc.name.toLowerCase().includes(q) || poc.phone.includes(query!)
    })
    .map(poc => ({ phone: poc.phone, name: poc.name, saved: true }))

  return [...savedOptions, ...rollOptions]
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
          :key="`${option.saved ? 'saved' : 'roll'}-${option.phone}`"
          role="option"
          :aria-selected="index === highlighted"
          class="flex cursor-pointer items-center justify-between gap-3 rounded-md px-2 py-1.5 text-sm"
          :class="index === highlighted ? 'bg-elevated text-highlighted' : 'text-default hover:bg-elevated/50'"
          @pointerdown.prevent
          @click="select(option)"
          @pointermove="highlighted = index"
        >
          <span class="truncate">{{ option.name }}</span>
          <span class="shrink-0 text-xs text-dimmed">
            <template v-if="option.saved">Saved · </template>{{ option.phone }}
          </span>
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
