<script setup lang="ts">
const model = defineModel<string>()

const normalized = computed({
  get: () => model.value?.substring(2),
  set: (value) => {
    model.value = '65' + value
  },
})
</script>

<template>
  <UInput
    v-model="normalized"
    v-bind="$attrs"
  />
</template>
