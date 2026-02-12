import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AuthUtils } from 'app/core/auth/auth.utils';
import { UserService } from 'app/core/user/user.service';
import { Observable, of, switchMap, throwError } from 'rxjs';

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class AuthService {
    private _authenticated: boolean = false;
    private _httpClient = inject(HttpClient);
    private _userService = inject(UserService);

    // -----------------------------------------------------------------------------------------------------
    // @ Accessors
    // -----------------------------------------------------------------------------------------------------

    /**
     * Setter & getter for access token
     */
    set accessToken(token: string) {
        localStorage.setItem('accessToken', token);
    }

    get accessToken(): string {
        return localStorage.getItem('accessToken') ?? '';
    }

    // -----------------------------------------------------------------------------------------------------
    // @ Public methods
    // -----------------------------------------------------------------------------------------------------

    /**
     * Request OTP code to be sent to the email.
     *
     * @param email
     */
    requestOtp(email: string): Observable<any> {
        return this._httpClient.post(`${API_BASE}/auth/otp`, { email });
    }

    /**
     * Compatibility method used by template modules.
     * Internally maps to OTP request.
     */
    forgotPassword(email: string): Observable<any> {
        return this.requestOtp(email);
    }

    /**
     * Compatibility method used by template modules.
     * Not used in OTP flow.
     */
    resetPassword(password: string): Observable<any> {
        return of({ success: true });
    }

    /**
     * Sign in (OTP-only). The "password" field is treated as the OTP code.
     *
     * @param credentials
     */
    signIn(credentials: { email: string; password: string }): Observable<any> {
        // Throw error, if the user is already logged in
        if (this._authenticated) {
            return throwError('User is already logged in.');
        }

        return this._httpClient.post(`${API_BASE}/auth/sign-in`, credentials).pipe(
            switchMap((response: any) => {
                // Store the access token in the local storage
                this.accessToken = response.accessToken ?? '';
                if (response.refreshToken) {
                    localStorage.setItem('refreshToken', response.refreshToken);
                }

                // Set the authenticated flag to true
                this._authenticated = true;

                // Store the user on the user service
                this._userService.user = {
                    id: credentials.email,
                    name: credentials.email.split('@')[0] ?? credentials.email,
                    email: credentials.email,
                    status: 'online',
                };

                // Return a new observable with the response
                return of(response);
            })
        );
    }

    /**
     * Sign in using the access token
     */
    signInUsingToken(): Observable<any> {
        if (!this.accessToken || AuthUtils.isTokenExpired(this.accessToken)) {
            return of(false);
        }

        const claims = this._tryDecodeJwt(this.accessToken);
        const email =
            (claims?.email as string | undefined) ||
            (claims?.preferred_username as string | undefined) ||
            (claims?.upn as string | undefined) ||
            '';

        this._authenticated = true;
        this._userService.user = {
            id: email || 'user',
            name: email ? email.split('@')[0] : 'Usuario',
            email: email,
            status: 'online',
        };

        return of(true);
    }

    /**
     * Sign out
     */
    signOut(): Observable<any> {
        // Remove the access token from the local storage
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');

        // Set the authenticated flag to false
        this._authenticated = false;

        // Return the observable
        return of(true);
    }

    /**
     * Sign up
     *
     * @param user
     */
    signUp(user: {
        name: string;
        email: string;
        password: string;
        company: string;
    }): Observable<any> {
        return this._httpClient.post('api/auth/sign-up', user);
    }

    /**
     * Unlock session
     *
     * @param credentials
     */
    unlockSession(credentials: {
        email: string;
        password: string;
    }): Observable<any> {
        return this._httpClient.post('api/auth/unlock-session', credentials);
    }

    /**
     * Check the authentication status
     */
    check(): Observable<boolean> {
        // Check if the user is logged in
        if (this._authenticated) {
            return of(true);
        }

        // Check the access token availability
        if (!this.accessToken) {
            return of(false);
        }

        // Check the access token expire date
        if (AuthUtils.isTokenExpired(this.accessToken)) {
            return of(false);
        }

        // If the access token exists, and it didn't expire, sign in using it
        return this.signInUsingToken();
    }

    private _tryDecodeJwt(token: string): any | null {
        try {
            const parts = token.split('.');
            if (parts.length !== 3) {
                return null;
            }

            const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
            const jsonPayload = decodeURIComponent(
                atob(base64)
                    .split('')
                    .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
                    .join('')
            );
            return JSON.parse(jsonPayload);
        } catch {
            return null;
        }
    }
}
