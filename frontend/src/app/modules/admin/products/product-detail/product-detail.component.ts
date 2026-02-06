import {
    ChangeDetectionStrategy,
    Component,
    computed,
    effect,
    inject,
    signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { finalize } from 'rxjs/operators';
import { map } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import {
    CompetitorInfo,
    ProductCompetitorPrice,
    ProductDetailResponse,
    ProductHistoryPoint,
    ProductHistoryPrice,
    ProductsService,
} from 'app/core/products/products.service';

interface CompetitorDelta {
    amount: number | null;
    percent: number | null;
}

interface CompetitorRowView {
    competitor: CompetitorInfo;
    latest: ProductCompetitorPrice | null;
    listDelta: CompetitorDelta;
    promoDelta: CompetitorDelta;
}

interface HistoryPointView extends ProductHistoryPoint {
    pricesByCompetitor: Record<number, ProductHistoryPrice>;
}

@Component({
    selector: 'app-product-detail',
    templateUrl: './product-detail.component.html',
    styleUrls: ['./product-detail.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        CommonModule,
        RouterLink,
        MatButtonModule,
        MatFormFieldModule,
        MatInputModule,
    ],
})
export class ProductDetailComponent {
    private _service = inject(ProductsService);
    private _route = inject(ActivatedRoute);

    readonly productId = toSignal(
        this._route.paramMap.pipe(
            map((params) => Number(params.get('id')) || 0)
        ),
        { initialValue: 0 }
    );

    readonly loading = signal(false);
    readonly error = signal('');
    readonly data = signal<ProductDetailResponse | null>(null);
    readonly urlDrafts = signal<Record<number, string>>({});
    readonly savingIds = signal<Set<number>>(new Set());

    readonly snapshotDate = computed(() => this.data()?.snapshotDate ?? '—');
    readonly product = computed(() => this.data()?.product ?? null);
    readonly competitors = computed<CompetitorInfo[]>(
        () => this.data()?.competitors ?? []
    );

    readonly competitorRows = computed<CompetitorRowView[]>(() => {
        const data = this.data();
        if (!data) {
            return [];
        }

        const latestByCompetitor = new Map<number, ProductCompetitorPrice>();
        for (const entry of data.latest) {
            latestByCompetitor.set(entry.competitorId, entry);
        }

        const baseListPrice = data.product.medipielListPrice ?? null;
        const basePromoPrice = data.product.medipielPromoPrice ?? null;

        return data.competitors.map((competitor) => {
            const latest = latestByCompetitor.get(competitor.id) ?? null;
            return {
                competitor,
                latest,
                listDelta: this.computeDelta(baseListPrice, latest?.listPrice ?? null),
                promoDelta: this.computeDelta(basePromoPrice, latest?.promoPrice ?? null),
            };
        });
    });

    readonly historyView = computed<HistoryPointView[]>(() => {
        const history = this.data()?.history ?? [];
        return history.map((point) => {
            const pricesByCompetitor: Record<number, ProductHistoryPrice> = {};
            for (const price of point.prices) {
                pricesByCompetitor[price.competitorId] = price;
            }
            return {
                ...point,
                pricesByCompetitor,
            };
        });
    });

    constructor() {
        effect(() => {
            const id = this.productId();
            if (id > 0) {
                this.load(id);
            }
        });
    }

    load(productId: number): void {
        this.loading.set(true);
        this.error.set('');

        this._service
            .getProductDetail(productId, 7)
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (data) => {
                    this.data.set(data);
                    this.syncUrlDrafts(data);
                },
                error: () =>
                    this.error.set('No se pudo cargar el detalle del producto.'),
            });
    }

    refresh(): void {
        const id = this.productId();
        if (id > 0) {
            this.load(id);
        }
    }

    onUrlChange(competitorId: number, value: string): void {
        const draft = { ...this.urlDrafts() };
        draft[competitorId] = value;
        this.urlDrafts.set(draft);
    }

    saveUrl(competitorId: number): void {
        const productId = this.productId();
        if (!productId) {
            return;
        }

        const url = (this.urlDrafts()[competitorId] ?? '').trim();
        if (!url) {
            this.error.set('Ingresa una URL valida para guardar.');
            return;
        }

        this.setSaving(competitorId, true);
        this._service
            .updateCompetitorUrl(productId, competitorId, url)
            .pipe(finalize(() => this.setSaving(competitorId, false)))
            .subscribe({
                next: (updated) => {
                    this.error.set('');
                    this.data.update((current) => {
                        if (!current) {
                            return current;
                        }
                        const latest = current.latest.map((item) =>
                            item.competitorId === competitorId
                                ? {
                                      ...item,
                                      url: updated.url,
                                      matchMethod: updated.matchMethod,
                                      matchScore: updated.matchScore,
                                      lastMatchedAt: updated.lastMatchedAt,
                                  }
                                : item
                        );
                        return {
                            ...current,
                            latest,
                        };
                    });
                },
                error: () =>
                    this.error.set('No se pudo guardar la URL manual.'),
            });
    }

    getUrlDraft(competitorId: number, fallback: string | null): string {
        return this.urlDrafts()[competitorId] ?? fallback ?? '';
    }

    isSaving(competitorId: number): boolean {
        return this.savingIds().has(competitorId);
    }

    formatMatchMethod(method: string | null | undefined): string {
        if (!method) {
            return '—';
        }

        const normalized = method.trim().toLowerCase();
        switch (normalized) {
            case 'ean':
                return 'EAN';
            case 'name':
                return 'Nombre';
            case 'ai':
                return 'IA';
            case 'manual':
                return 'Manual';
            case 'no_match':
                return 'Sin match';
            default:
                return '—';
        }
    }

    isLargeDelta(delta: CompetitorDelta): boolean {
        if (delta.percent === null || delta.percent === undefined) {
            return false;
        }
        return Math.abs(delta.percent) >= 30;
    }

    private computeDelta(
        basePrice: number | null,
        competitorPrice: number | null
    ): CompetitorDelta {
        if (basePrice === null || competitorPrice === null || basePrice === 0) {
            return {
                amount: null,
                percent: null,
            };
        }

        const amount = competitorPrice - basePrice;
        const percent = (amount / basePrice) * 100;
        return { amount, percent };
    }

    private setSaving(competitorId: number, saving: boolean): void {
        const next = new Set(this.savingIds());
        if (saving) {
            next.add(competitorId);
        } else {
            next.delete(competitorId);
        }
        this.savingIds.set(next);
    }

    private syncUrlDrafts(data: ProductDetailResponse): void {
        const latestByCompetitor = new Map<number, ProductCompetitorPrice>();
        for (const entry of data.latest) {
            latestByCompetitor.set(entry.competitorId, entry);
        }

        const draft: Record<number, string> = {};
        for (const competitor of data.competitors) {
            const url = latestByCompetitor.get(competitor.id)?.url ?? '';
            draft[competitor.id] = url;
        }
        this.urlDrafts.set(draft);
    }
}
