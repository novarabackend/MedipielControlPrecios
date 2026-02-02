import {
    ChangeDetectionStrategy,
    Component,
    computed,
    inject,
    signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { finalize } from 'rxjs/operators';
import {
    SchedulerService,
    SchedulerSettings,
    SchedulerStatus,
} from 'app/core/scheduler/scheduler.service';

interface DayOption {
    label: string;
    name: string;
    bit: number;
}

const DAYS: DayOption[] = [
    { label: 'L', name: 'Lunes', bit: 1 },
    { label: 'M', name: 'Martes', bit: 2 },
    { label: 'M', name: 'Miercoles', bit: 4 },
    { label: 'J', name: 'Jueves', bit: 8 },
    { label: 'V', name: 'Viernes', bit: 16 },
    { label: 'S', name: 'Sabado', bit: 32 },
    { label: 'D', name: 'Domingo', bit: 64 },
];

@Component({
    selector: 'app-scheduler',
    templateUrl: './scheduler.component.html',
    styleUrls: ['./scheduler.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        ReactiveFormsModule,
        MatButtonModule,
        MatFormFieldModule,
        MatIconModule,
        MatInputModule,
        MatSlideToggleModule,
    ],
})
export class SchedulerComponent {
    private _fb = inject(FormBuilder);
    private _service = inject(SchedulerService);

    readonly settings = signal<SchedulerSettings | null>(null);
    readonly status = signal<SchedulerStatus | null>(null);
    readonly loading = signal(false);
    readonly saving = signal(false);
    readonly error = signal('');
    readonly daysMask = signal(127);
    readonly days = DAYS;

    readonly form = this._fb.group({
        dailyTime: ['06:00', [Validators.required]],
        enabled: [true],
    });

    readonly hasError = computed(() => this.error().length > 0);
    readonly isRunning = computed(() => this.status()?.running ?? false);
    readonly statusLabel = computed(() =>
        this.isRunning() ? 'En ejecucion' : 'Disponible'
    );

    constructor() {
        this.load();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');

        this._service
            .getSettings()
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (settings) => {
                    this.settings.set(settings);
                    this.form.patchValue({
                        dailyTime: settings.dailyTime,
                        enabled: settings.enabled,
                    });
                    this.daysMask.set(settings.daysOfWeekMask);
                    this.loadStatus();
                },
                error: () => {
                    this.error.set('No se pudo cargar la configuracion.');
                },
            });
    }

    loadStatus(): void {
        this._service.getStatus().subscribe({
            next: (status) => this.status.set(status),
            error: () => this.error.set('No se pudo cargar el estado del scheduler.'),
        });
    }

    toggleDay(bit: number): void {
        this.daysMask.update((mask) => mask ^ bit);
    }

    isDaySelected(bit: number): boolean {
        return (this.daysMask() & bit) !== 0;
    }

    save(): void {
        if (this.form.invalid) {
            this.form.markAllAsTouched();
            return;
        }

        if (this.daysMask() === 0) {
            this.error.set('Selecciona al menos un dia para ejecutar.');
            return;
        }

        const dailyTime = this.form.value.dailyTime ?? '06:00';
        const enabled = this.form.value.enabled ?? false;

        this.saving.set(true);
        this.error.set('');

        this._service
            .updateSettings({
                dailyTime,
                daysOfWeekMask: this.daysMask(),
                enabled,
            })
            .pipe(finalize(() => this.saving.set(false)))
            .subscribe({
                next: (settings) => {
                    this.settings.set(settings);
                    this.loadStatus();
                },
                error: (error) => {
                    if (error?.status === 400) {
                        this.error.set('Verifica la hora y los dias seleccionados.');
                        return;
                    }
                    this.error.set('No se pudo guardar la configuracion.');
                },
            });
    }

    runNow(): void {
        if (this.isRunning()) {
            return;
        }

        this.saving.set(true);
        this.error.set('');

        this._service
            .runManual()
            .pipe(finalize(() => this.saving.set(false)))
            .subscribe({
                next: () => this.loadStatus(),
                error: (error) => {
                    if (error?.status === 400) {
                        this.error.set(
                            error?.error ??
                                'No se puede ejecutar el proceso sin productos cargados.'
                        );
                        this.loadStatus();
                        return;
                    }
                    if (error?.status === 409) {
                        this.error.set('Ya hay una corrida en ejecucion.');
                        this.loadStatus();
                        return;
                    }
                    this.error.set('No se pudo lanzar la corrida manual.');
                },
            });
    }
}
