export function useApi() {
  const locale = localStorage.getItem('locale') || 'tr'
  const token = localStorage.getItem('token')

  const headers = {
    'Accept': 'application/json',
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${token}`,
    'Accept-Language': locale,
  }

  return { headers, token }
}
