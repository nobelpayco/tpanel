// Brand adını blade <meta name="app-brand"> tag'inden okur
let cachedBrand = null

export function getBrand() {
  if (cachedBrand !== null) return cachedBrand
  const meta = typeof document !== 'undefined' ? document.querySelector('meta[name="app-brand"]') : null
  cachedBrand = (meta && meta.getAttribute('content')) || 'NobelPay'
  return cachedBrand
}

export function useBrand() {
  return getBrand()
}
