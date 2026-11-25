import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { installUiScale } from './services/uiScale'

// Chữ và khoảng cách co theo bề rộng màn hình (màn 1920 px to hơn 25%)
installUiScale()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
