import { toast } from 'vue3-toastify'

export function useSnackbar() {
  const success = (msg) => {
    toast.success(msg, { position: 'top-right', autoClose: 3000, theme: 'colored' })
  }

  const error = (msg) => {
    toast.error(msg, { position: 'top-right', autoClose: 5000, theme: 'colored' })
  }

  const warning = (msg) => {
    toast.warning(msg, { position: 'top-right', autoClose: 4000, theme: 'colored' })
  }

  // API response hatalarını parse edip göster
  const handleError = (data) => {
    if (data.errors) {
      Object.values(data.errors).flat().forEach(msg => error(msg))
    } else if (data.message) {
      error(data.message)
    }
  }

  return { success, error, warning, handleError }
}
