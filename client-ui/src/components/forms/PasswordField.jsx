import { useState, forwardRef } from 'react';
import { IconButton, InputAdornment } from '@mui/material';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import CustomTextField from './theme-elements/CustomTextField.jsx';

/**
 * Password input with a built-in show/hide toggle.
 *
 * Forwards every prop to <CustomTextField> so callers can keep using `value`,
 * `onChange`, `fullWidth`, `required`, etc. exactly the same way.
 *
 * The toggle is keyboard-skipped (tabIndex=-1) so it doesn't get in the way
 * during normal form navigation.
 */
const PasswordField = forwardRef(function PasswordField(
  { InputProps, ...rest },
  ref,
) {
  const [show, setShow] = useState(false);

  return (
    <CustomTextField
      {...rest}
      ref={ref}
      type={show ? 'text' : 'password'}
      InputProps={{
        ...InputProps,
        endAdornment: (
          <InputAdornment position="end">
            <IconButton
              aria-label={show ? 'Hide password' : 'Show password'}
              onClick={() => setShow((v) => !v)}
              onMouseDown={(e) => e.preventDefault()}
              edge="end"
              size="small"
              tabIndex={-1}
            >
              {show ? <VisibilityOffIcon /> : <VisibilityIcon />}
            </IconButton>
          </InputAdornment>
        ),
      }}
    />
  );
});

export default PasswordField;
