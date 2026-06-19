<script setup>
import { useI18n } from 'vue-i18n'

const { locale } = useI18n()

const languages = [
  { code: 'tr', label: 'Türkçe', flag: '🇹🇷' },
  { code: 'en', label: 'English', flag: '🇬🇧' },
  { code: 'ru', label: 'Русский', flag: '🇷🇺' },
  { code: 'ur', label: 'اردو', flag: '🇵🇰' },
]

const current = computed(() => languages.find(l => l.code === locale.value))

const changeLocale = code => {
  locale.value = code
  localStorage.setItem('locale', code)
}
</script>

<template>
  <VMenu offset="14px">
    <template #activator="{ props }">
      <VBtn
        v-bind="props"
        variant="tonal"
        size="default"
        class="px-4"
      >
        <span
          class="me-2"
          style="font-size: 1.25rem;"
        >{{ current?.flag }}</span>
        <span class="text-body-1 font-weight-medium">{{ current?.label }}</span>
        <VIcon
          end
          icon="tabler-chevron-down"
          size="18"
        />
      </VBtn>
    </template>

    <VList
      min-width="150"
      density="compact"
    >
      <VListItem
        v-for="lang in languages"
        :key="lang.code"
        :active="locale === lang.code"
        color="primary"
        @click="changeLocale(lang.code)"
      >
        <template #prepend>
          <span class="me-2">{{ lang.flag }}</span>
        </template>
        <VListItemTitle>{{ lang.label }}</VListItemTitle>
      </VListItem>
    </VList>
  </VMenu>
</template>
