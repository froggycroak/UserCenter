import { createRouter, createWebHistory } from 'vue-router'
import Login from './pages/Login.vue'
import Register from './pages/Register.vue'
import Edit from './pages/Edit.vue'
import Toolkit from './pages/Toolkit.vue'
import Account from './pages/Account.vue'
import Assistant from './pages/Assistant.vue'
import Support from './pages/Support.vue'
import { getAccessToken } from './api'
import { openAuthDialog } from './authDialog'
import { isEmbed } from './embed'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', redirect: '/toolkit' },
    { path: '/login', component: Login },
    { path: '/register', component: Register },
    { path: '/edit', component: Edit, meta: { auth: true } },
    { path: '/toolkit', component: Toolkit },
    { path: '/external', component: Assistant },
    { path: '/assistant', redirect: '/external' },
    { path: '/account', component: Account },
    { path: '/support', component: Support },
    { path: '/home', redirect: '/toolkit' },
    { path: '/apps', redirect: '/toolkit' },
    { path: '/workstation', redirect: '/external' }
  ]
})

router.beforeEach((to) => {
  if (to.meta.auth && !getAccessToken()) {
    if (to.path === '/edit' && isEmbed(to.query)) return true
    if (isEmbed(to.query)) {
      const query: Record<string, string> = { returnUrl: to.fullPath, embed: '1' }
      const parentOrigin = to.query.parentOrigin
      if (typeof parentOrigin === 'string' && parentOrigin) {
        query.parentOrigin = parentOrigin
      }
      return { path: '/login', query }
    }
    openAuthDialog('/login', to.fullPath)
  }
  return true
})

export default router
