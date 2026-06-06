import { useAuth } from '../context/AuthContext';

/**
 * Renders children only when the current user has *all* of the listed
 * permission codes (or roles). Useful for hiding nav items, buttons,
 * and admin-only sections in the UI.
 *
 * Note: this is UI-only. Backend permission checks remain authoritative —
 * a user can still attempt API calls that the backend will reject 403.
 *
 * Usage:
 *   <RequirePermission permission="Users.Read">...</RequirePermission>
 *   <RequirePermission anyRole={["Admin"]}>...</RequirePermission>
 */
const RequirePermission = ({ permission, anyPermission, anyRole, children, fallback = null }) => {
  const { hasPermission, hasRole, permissions } = useAuth();

  if (permission && !hasPermission(permission)) return fallback;
  if (anyPermission && !anyPermission.some((c) => hasPermission(c))) return fallback;
  if (anyRole && !anyRole.some((r) => hasRole(r))) return fallback;

  // If nothing was passed, treat as "any authenticated user" (just verify we have a permission set).
  if (!permission && !anyPermission && !anyRole && permissions == null) return fallback;

  return children;
};

export default RequirePermission;
