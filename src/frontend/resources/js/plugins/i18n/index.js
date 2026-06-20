import { createI18n } from 'vue-i18n'
import tr from './locales/tr.json'
import en from './locales/en.json'
import ru from './locales/ru.json'
import ur from './locales/ur.json'

const savedLocale = localStorage.getItem('locale') || 'tr'

export const i18n = createI18n({
  legacy: false,
  locale: savedLocale,
  fallbackLocale: 'en',
  messages: { tr, en, ru, ur },
})

export default function (app) {
  app.use(i18n)
}
