import api from '../api/axiosInstance';

// Redirects the browser to Stripe Checkout. Replaces the manual set-plan upgrade.
export async function startCheckout() {
  const res = await api.post('/Billing/create-checkout-session');
  const url = res.data?.result?.url;
  if (url) window.location.href = url;
}

// Redirects to the Stripe Customer Portal (manage/cancel).
export async function openBillingPortal() {
  const res = await api.post('/Billing/create-portal-session');
  const url = res.data?.result?.url;
  if (url) window.location.href = url;
}
