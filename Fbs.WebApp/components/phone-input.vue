<script setup lang="ts">
const model = defineModel<string>()

const bind = computed({
  get() {
    return model.value?.substring(2)
  },
  set(value) {
    model.value = '65' + value
  },
})
</script>

<template>
  <InputText
    v-bind="$attrs"
    v-model="bind"
  />
</template>
