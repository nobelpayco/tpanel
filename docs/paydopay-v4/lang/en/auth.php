<?php

return [
    'failed'   => 'These credentials do not match our records.',
    'password' => 'The provided password is incorrect.',
    'throttle' => 'Too many login attempts. Please try again in :seconds seconds.',

    // Custom — AuthController + EnforceIdleTimeout
    'invalid_credentials'      => 'Invalid username or password.',
    'account_blocked'          => 'Your account is blocked.',
    'two_factor_required'      => 'Two-factor authentication required.',
    'session_expired'          => 'Session expired.',
    'invalid_token'            => 'Invalid token.',
    'invalid_code'             => 'Invalid verification code.',
    'logged_out'               => 'Logged out.',
    'current_password_wrong'   => 'Current password is incorrect.',
    'password_updated_reauth'  => 'Password updated. Please log in again.',
    'pw_min'                   => 'New password must be at least 6 characters long.',
    'pw_regex'                 => 'New password must contain at least one letter and one digit.',
    'pw_different'             => 'New password cannot be the same as the old password.',
    'idle_timeout'             => 'Your session was ended due to inactivity.',
    'no_permission'            => 'You do not have permission for this action.',
];
