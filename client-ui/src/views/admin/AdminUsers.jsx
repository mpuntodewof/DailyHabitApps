import { useEffect, useState } from 'react';
import {
    Box, Card, CardContent, Typography, Table, TableBody, TableCell,
    TableContainer, TableHead, TableRow, Chip, CircularProgress, Alert,
    Stack, Menu, MenuItem, IconButton
} from '@mui/material';
import AddIcon from '@mui/icons-material/AddCircleOutline';
import CancelIcon from '@mui/icons-material/Cancel';
import PageContainer from '../../components/container/PageContainer';
import api from '../../api/axiosInstance';

const AdminUsers = () => {
    const [users, setUsers] = useState([]);
    const [roles, setRoles] = useState([]);
    const [canManageRoles, setCanManageRoles] = useState(false);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    const [menuAnchor, setMenuAnchor] = useState(null);
    const [menuUserId, setMenuUserId] = useState(null);

    const fetchAll = async () => {
        setLoading(true);
        setError(null);
        try {
            const usersRes = await api.get('/Admin/users');
            setUsers(usersRes.data?.result || []);

            try {
                const rolesRes = await api.get('/Admin/roles');
                setRoles(rolesRes.data?.result || []);
                setCanManageRoles(true);
            } catch (err) {
                // 403 just means no Roles.Read permission — viewer mode.
                if (err?.response?.status !== 403) throw err;
                setCanManageRoles(false);
            }
        } catch (err) {
            if (err?.response?.status === 403) {
                setError("You don't have permission to view this page.");
            } else {
                setError(err?.response?.data?.errorMessages?.[0] || 'Failed to load users');
            }
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { fetchAll(); }, []);

    const handleAssign = async (userId, roleName) => {
        try {
            await api.post(`/Admin/users/${userId}/roles/${roleName}`);
            await fetchAll();
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || `Failed to assign ${roleName}`);
        } finally {
            setMenuAnchor(null);
        }
    };

    const handleRevoke = async (userId, roleName) => {
        try {
            await api.delete(`/Admin/users/${userId}/roles/${roleName}`);
            await fetchAll();
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || `Failed to revoke ${roleName}`);
        }
    };

    const openMenu = (e, userId) => {
        setMenuAnchor(e.currentTarget);
        setMenuUserId(userId);
    };

    return (
        <PageContainer title="Admin · Users" description="User management">
            <Card>
                <CardContent>
                    <Typography variant="h3" mb={3}>Users</Typography>

                    {loading && (
                        <Box display="flex" justifyContent="center" py={4}>
                            <CircularProgress />
                        </Box>
                    )}

                    {!loading && error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

                    {!loading && !error && (
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>ID</TableCell>
                                        <TableCell>Username</TableCell>
                                        <TableCell>Email</TableCell>
                                        <TableCell>Active</TableCell>
                                        <TableCell>Roles</TableCell>
                                        <TableCell>Created</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {users.map((u) => {
                                        const userRoles = u.roles || [];
                                        const availableToAdd = roles
                                            .map((r) => r.name)
                                            .filter((name) => !userRoles.includes(name));
                                        return (
                                            <TableRow key={u.id}>
                                                <TableCell>{u.id}</TableCell>
                                                <TableCell>{u.username}</TableCell>
                                                <TableCell>{u.email}</TableCell>
                                                <TableCell>
                                                    <Chip
                                                        label={u.isActive ? 'Yes' : 'No'}
                                                        color={u.isActive ? 'success' : 'default'}
                                                        size="small"
                                                    />
                                                </TableCell>
                                                <TableCell>
                                                    <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', gap: 0.5 }} alignItems="center">
                                                        {userRoles.map((r) => (
                                                            <Chip
                                                                key={r}
                                                                label={r}
                                                                size="small"
                                                                onDelete={canManageRoles ? () => handleRevoke(u.id, r) : undefined}
                                                                deleteIcon={<CancelIcon />}
                                                            />
                                                        ))}
                                                        {canManageRoles && availableToAdd.length > 0 && (
                                                            <IconButton size="small" onClick={(e) => openMenu(e, u.id)} aria-label="add role">
                                                                <AddIcon fontSize="small" />
                                                            </IconButton>
                                                        )}
                                                    </Stack>
                                                </TableCell>
                                                <TableCell>{u.createdAt ? new Date(u.createdAt).toLocaleDateString() : '—'}</TableCell>
                                            </TableRow>
                                        );
                                    })}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}

                    <Menu
                        anchorEl={menuAnchor}
                        open={Boolean(menuAnchor)}
                        onClose={() => setMenuAnchor(null)}
                    >
                        {(roles
                            .map((r) => r.name)
                            .filter((name) => {
                                const u = users.find((x) => x.id === menuUserId);
                                return u && !(u.roles || []).includes(name);
                            }))
                            .map((roleName) => (
                                <MenuItem key={roleName} onClick={() => handleAssign(menuUserId, roleName)}>
                                    Assign {roleName}
                                </MenuItem>
                            ))}
                    </Menu>
                </CardContent>
            </Card>
        </PageContainer>
    );
};

export default AdminUsers;
