<script setup>
import { useI18n } from 'vue-i18n'
import { useBrand } from '@/composables/useBrand'

const { t } = useI18n()
const brand = useBrand()
const showLicense = ref(false)
</script>

<template>
  <div class="h-100 d-flex align-center justify-md-space-between justify-center">
    <span class="d-flex align-center text-medium-emphasis">
      &copy;
      {{ new Date().getFullYear() }}
      <span v-if="brand === 'Paylira'" class="d-flex align-center">
        Made With
        <VIcon
          icon="tabler-heart-filled"
          color="error"
          size="1.25rem"
          class="mx-1"
        />
        By <span class="text-primary ms-1 font-weight-medium">Heavpear</span>
      </span>
      <span v-else class="ms-1 font-weight-medium">{{ brand }}</span>
    </span>

    <span class="d-md-flex gap-x-4 text-primary d-none">
      <a
        class="cursor-pointer"
        @click="showLicense = true"
      >{{ t('footer.license') }}</a>
      <a
        v-if="brand === 'Paylira'"
        href="https://t.me/heavpear"
        target="_blank"
        rel="noopener noreferrer"
      >{{ t('footer.support') }}</a>
    </span>
  </div>

  <VDialog v-model="showLicense" max-width="550">
    <VCard>
      <VCardItem>
        <VCardTitle class="d-flex align-center gap-2">
          <VIcon icon="tabler-shield-lock" color="primary" />
          {{ t('footer.license') }}
        </VCardTitle>
      </VCardItem>
      <VDivider />
      <VCardText class="text-body-1">
        {{ t('footer.license_text', { brand }) }}
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn color="primary" @click="showLicense = false">{{ t('common.confirm') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
