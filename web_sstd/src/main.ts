import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import './styles.css'

const title = (import.meta.env.VITE_SITE_TITLE || '').trim()
if (title) document.title = title

createApp(App).use(router).mount('#app')
