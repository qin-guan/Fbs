<script setup lang="ts">
const model = defineModel<string>()

const bind = computed({
  get() {
    if (!model.value) {
      return ''
    }

    if (model.value.startsWith('65')) {
      return model.value?.substring(2)
    }

    return model.value
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
