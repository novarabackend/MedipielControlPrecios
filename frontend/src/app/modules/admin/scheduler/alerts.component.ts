import { CommonModule } from '@angular/common';
import {
    ChangeDetectionStrategy,
    Component,
    computed,
    inject,
    signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { finalize, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
    AlertRule,
    AlertsService,
} from 'app/core/alerts/alerts.service';
import { MastersService } from 'app/core/masters/masters.service';

interface AlertRuleRow {
    brandId: number;
    brandName: string;
    listThreshold: number | null;
    promoThreshold: number | null;
    active: boolean;
}

@Component({
    selector: 'app-alerts-settings',
    templateUrl: './alerts.component.html',
    styleUrls: ['./alerts.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        CommonModule,
        FormsModule,
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSlideToggleModule,
    ],
})
export class AlertsSettingsComponent {
    private _masters = inject(MastersService);
    private _alerts = inject(AlertsService);

    readonly loading = signal(false);
    readonly saving = signal(false);
    readonly error = signal('');
    readonly rows = signal<AlertRuleRow[]>([]);

    readonly hasError = computed(() => this.error().length > 0);

    constructor() {
        this.load();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');

        forkJoin({
            brands: this._masters.getBrands().pipe(catchError(() => of([]))),
            rules: this._alerts.getRules().pipe(catchError(() => of([]))),
        })
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: ({ brands, rules }) => {
                    const ruleMap = new Map<number, AlertRule>();
                    for (const rule of rules) {
                        ruleMap.set(rule.brandId, rule);
                    }

                    const mapped = brands.map((brand) => {
                        const rule = ruleMap.get(brand.id);
                        return {
                            brandId: brand.id,
                            brandName: brand.name,
                            listThreshold: rule?.listPriceThresholdPercent ?? null,
                            promoThreshold: rule?.promoPriceThresholdPercent ?? null,
                            active: rule?.active ?? false,
                        } satisfies AlertRuleRow;
                    });

                    this.rows.set(mapped);
                },
                error: () => {
                    this.error.set('No se pudo cargar la configuracion de alertas.');
                },
            });
    }

    save(): void {
        const rows = this.rows();
        if (rows.length === 0) {
            return;
        }

        this.saving.set(true);
        this.error.set('');

        const requests = rows.map((row) =>
            this._alerts.upsertRule(row.brandId, {
                listPriceThresholdPercent: this.parsePercent(row.listThreshold),
                promoPriceThresholdPercent: this.parsePercent(row.promoThreshold),
                active: row.active,
            })
        );

        forkJoin(requests)
            .pipe(finalize(() => this.saving.set(false)))
            .subscribe({
                next: () => this.load(),
                error: () => {
                    this.error.set('No se pudo guardar la configuracion de alertas.');
                },
            });
    }

    private parsePercent(value: number | null): number | null {
        if (value === null || value === undefined) {
            return null;
        }
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : null;
    }
}
