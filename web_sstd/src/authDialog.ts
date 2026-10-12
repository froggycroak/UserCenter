import { reactive } from 'vue'

export type AuthDialogPath = '/login' | '/register' | '/edit'

export const authDialog = reactive({
  open: false,
  path: '/login' as AuthDialogPath,
  returnUrl: ''
})

const listeners = new Set<() => void>()

export function onAuthChanged(fn: () => void) {
  listeners.add(fn)
  return () => {
    listeners.delete(fn)
  }
}

export function notifyAuthChanged() {
  for (const fn of listeners) fn()
}

export function openAuthDialog(path: AuthDialogPath = '/login', returnUrl = '') {
  authDialog.path = path
  authDialog.returnUrl = returnUrl
  authDialog.open = true
}

export function closeAuthDialog() {
  authDialog.open = false
  authDialog.returnUrl = ''
}
