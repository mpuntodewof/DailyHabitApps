import { useAuth } from '../context/AuthContext';

/**
 * Renders children only when the current user has an ACTIVE Pro subscription.
 * UI-only — the backend [RequiresActiveSubscription] gate is authoritative.
 *
 * Usage:
 *   <RequirePro fallback={<UpgradePrompt />}>...</RequirePro>
 */
const RequirePro = ({ children, fallback = null }) => {
  const { isPro } = useAuth();
  if (!isPro) return fallback;
  return children;
};

export default RequirePro;
