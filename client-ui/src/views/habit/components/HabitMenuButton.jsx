import React, { useState, forwardRef } from 'react';
import {
    IconButton,
    Menu,
    MenuItem,
    Dialog,
    DialogTitle,
    DialogActions,
    Button,
    Box,
    Slide
} from '@mui/material';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import EditIcon from '@mui/icons-material/Edit';
import ClearIcon from '@mui/icons-material/Clear';
import ArchiveIcon from '@mui/icons-material/Archive';
import UnarchiveIcon from '@mui/icons-material/Unarchive';
import NotificationsIcon from '@mui/icons-material/Notifications';
import { IconPlayerSkipForward } from '@tabler/icons-react';
import { styled, alpha } from '@mui/material/styles';

const StyledMenu = styled((props) => (
    <Menu
        elevation={0}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        {...props}
    />
))(({ theme }) => ({
    '& .MuiPaper-root': {
        borderRadius: 6,
        marginTop: theme.spacing(1),
        minWidth: 180,
        color: 'rgb(55, 65, 81)',
        boxShadow:
            'rgb(255, 255, 255) 0px 0px 0px 0px, rgba(0, 0, 0, 0.05) 0px 0px 0px 1px, rgba(0, 0, 0, 0.1) 0px 10px 15px -3px, rgba(0, 0, 0, 0.05) 0px 4px 6px -2px',
        '& .MuiMenu-list': {
            padding: '4px 0',
        },
        '& .MuiMenuItem-root': {
            '& .MuiSvgIcon-root': {
                fontSize: 18,
                color: theme.palette.text.secondary,
                marginRight: theme.spacing(1.5),
            },
            '&:active': {
                backgroundColor: alpha(theme.palette.primary.main, theme.palette.action.selectedOpacity),
            },
        },
    },
}));

// Transition animation for confirmation dialog
const Transition = forwardRef(function Transition(props, ref) {
    return <Slide direction="up" ref={ref} {...props} />;
});

const HabitMenuButton = ({ onEdit, onDelete, onArchive, onRestore, onReminders, onSkip, isArchived = false }) => {
    const [anchorEl, setAnchorEl] = useState(null);
    const [confirmOpen, setConfirmOpen] = useState(false);
    const open = Boolean(anchorEl);

    const handleClick = (event) => {
        event.stopPropagation();
        setAnchorEl(event.currentTarget);
    };

    const handleClose = () => {
        setAnchorEl(null);
    };

    const handleEdit = () => {
        handleClose();
        onEdit && onEdit();
    };

    const handleDeleteClick = () => {
        handleClose();
        setConfirmOpen(true);
    };

    const handleConfirmDelete = () => {
        if (onDelete) onDelete(); // 🔥 Trigger the actual delete logic from parent (Habit.jsx)
        setConfirmOpen(false);
    };

    const handleCancelDelete = () => {
        setConfirmOpen(false);
    };

    return (
        <Box onClick={(e) => e.stopPropagation()}>
            <IconButton
                aria-label="more"
                aria-controls={open ? 'habit-menu' : undefined}
                aria-haspopup="true"
                onClick={handleClick}
                disableRipple
                sx={{
                    outline: 'none',
                    border: 'none',
                    '&:focus': { outline: 'none' },
                    '&:focus-visible': { outline: 'none' }
                }}
            >
                <MoreVertIcon />
            </IconButton>

            <StyledMenu id="habit-menu" anchorEl={anchorEl} open={open} onClose={handleClose}>
                <MenuItem onClick={handleEdit} disableRipple>
                    <EditIcon /> Edit
                </MenuItem>
                {onReminders && (
                    <MenuItem onClick={() => { handleClose(); onReminders(); }} disableRipple>
                        <NotificationsIcon /> Reminders
                    </MenuItem>
                )}
                {onSkip && (
                    <MenuItem onClick={() => { handleClose(); onSkip(); }} disableRipple>
                        <IconPlayerSkipForward size={18} style={{ marginRight: 12 }} /> Skip today
                    </MenuItem>
                )}
                {isArchived
                    ? onRestore && (
                        <MenuItem onClick={() => { handleClose(); onRestore(); }} disableRipple>
                            <UnarchiveIcon /> Restore
                        </MenuItem>
                    )
                    : onArchive && (
                        <MenuItem onClick={() => { handleClose(); onArchive(); }} disableRipple>
                            <ArchiveIcon /> Archive
                        </MenuItem>
                    )}
                <MenuItem onClick={handleDeleteClick} disableRipple>
                    <ClearIcon /> Delete
                </MenuItem>
            </StyledMenu>

            {/* Animated Confirm Delete Dialog */}
            <Dialog
                open={confirmOpen}
                onClose={handleCancelDelete}
                TransitionComponent={Transition}
                keepMounted
            >
                <DialogTitle>Are you sure you want to delete this habit?</DialogTitle>
                <DialogActions>
                    <Button onClick={handleCancelDelete} color="inherit">
                        Cancel
                    </Button>
                    <Button onClick={handleConfirmDelete} color="error" variant="contained">
                        Delete
                    </Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
};

export default HabitMenuButton;