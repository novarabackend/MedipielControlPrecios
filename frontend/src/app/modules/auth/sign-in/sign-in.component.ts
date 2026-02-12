import { Component, OnInit, ViewChild, ViewEncapsulation } from '@angular/core';
import {
    FormsModule,
    NgForm,
    ReactiveFormsModule,
    UntypedFormBuilder,
    UntypedFormGroup,
    Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { fuseAnimations } from '@fuse/animations';
import { FuseAlertComponent, FuseAlertType } from '@fuse/components/alert';
import { AuthService } from 'app/core/auth/auth.service';

@Component({
    selector: 'auth-sign-in',
    templateUrl: './sign-in.component.html',
    encapsulation: ViewEncapsulation.None,
    animations: fuseAnimations,
    imports: [
        FuseAlertComponent,
        FormsModule,
        ReactiveFormsModule,
        MatFormFieldModule,
        MatInputModule,
        MatButtonModule,
        MatProgressSpinnerModule,
    ],
})
export class AuthSignInComponent implements OnInit {
    @ViewChild('signInNgForm') signInNgForm: NgForm;

    alert: { type: FuseAlertType; message: string } = {
        type: 'success',
        message: '',
    };
    signInForm: UntypedFormGroup;
    showAlert: boolean = false;
    step: 'email' | 'code' = 'email';

    /**
     * Constructor
     */
    constructor(
        private _activatedRoute: ActivatedRoute,
        private _authService: AuthService,
        private _formBuilder: UntypedFormBuilder,
        private _router: Router
    ) {}

    // -----------------------------------------------------------------------------------------------------
    // @ Lifecycle hooks
    // -----------------------------------------------------------------------------------------------------

    /**
     * On init
     */
    ngOnInit(): void {
        // Create the form
        this.signInForm = this._formBuilder.group({
            email: ['', [Validators.required, Validators.email]],
            code: [''],
        });
    }

    // -----------------------------------------------------------------------------------------------------
    // @ Public methods
    // -----------------------------------------------------------------------------------------------------

    /**
     * Send OTP
     */
    sendOtp(): void {
        // Return if the form is invalid
        if (this.signInForm.get('email')?.invalid) {
            return;
        }

        // Disable the form
        this.signInForm.disable();

        // Hide the alert
        this.showAlert = false;

        const email = (this.signInForm.get('email')?.value as string).trim();

        this._authService.requestOtp(email).subscribe(
            () => {
                this.step = 'code';
                this.signInForm.enable();
                this.signInForm.get('code')?.setValidators([Validators.required]);
                this.signInForm.get('code')?.updateValueAndValidity();

                this.alert = {
                    type: 'success',
                    message:
                        'Te enviamos un codigo de acceso. Revisa tu correo y pegalo aqui.',
                };
                this.showAlert = true;
            },
            (response) => {
                // Re-enable the form
                this.signInForm.enable();

                // Set the alert
                this.alert = {
                    type: 'error',
                    message:
                        response?.error?.message ||
                        'No se pudo enviar el codigo. Intenta de nuevo.',
                };

                // Show the alert
                this.showAlert = true;
            }
        );
    }

    /**
     * Verify OTP and sign in
     */
    verifyOtp(): void {
        if (this.signInForm.get('email')?.invalid) {
            return;
        }

        if (this.signInForm.get('code')?.invalid) {
            return;
        }

        const email = (this.signInForm.get('email')?.value as string).trim();
        const code = (this.signInForm.get('code')?.value as string).trim();

        // Disable the form
        this.signInForm.disable();

        // Hide the alert
        this.showAlert = false;

        this._authService.signIn({ email, password: code }).subscribe(
            () => {
                const redirectURL =
                    this._activatedRoute.snapshot.queryParamMap.get(
                        'redirectURL'
                    ) || '/signed-in-redirect';

                this._router.navigateByUrl(redirectURL);
            },
            (response) => {
                // Re-enable the form
                this.signInForm.enable();

                // Set the alert
                this.alert = {
                    type: 'error',
                    message:
                        response?.error?.message ||
                        'Codigo invalido o expirado.',
                };

                // Show the alert
                this.showAlert = true;
            }
        );
    }

    backToEmail(): void {
        this.step = 'email';
        this.signInForm.get('code')?.setValue('');
        this.signInForm.get('code')?.clearValidators();
        this.signInForm.get('code')?.updateValueAndValidity();
    }
}
