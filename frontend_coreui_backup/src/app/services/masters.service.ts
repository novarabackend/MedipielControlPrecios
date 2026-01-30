import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface MasterItem {
  id: number;
  name: string;
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class MastersService {
  private readonly http = inject(HttpClient);

  getBrands(): Observable<MasterItem[]> {
    return this.http.get<MasterItem[]>(`${API_BASE}/brands`);
  }

  createBrand(name: string): Observable<MasterItem> {
    return this.http.post<MasterItem>(`${API_BASE}/brands`, { name });
  }

  updateBrand(id: number, name: string): Observable<MasterItem> {
    return this.http.put<MasterItem>(`${API_BASE}/brands/${id}`, { name });
  }

  deleteBrand(id: number): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/brands/${id}`);
  }

  getSuppliers(): Observable<MasterItem[]> {
    return this.http.get<MasterItem[]>(`${API_BASE}/suppliers`);
  }

  createSupplier(name: string): Observable<MasterItem> {
    return this.http.post<MasterItem>(`${API_BASE}/suppliers`, { name });
  }

  updateSupplier(id: number, name: string): Observable<MasterItem> {
    return this.http.put<MasterItem>(`${API_BASE}/suppliers/${id}`, { name });
  }

  deleteSupplier(id: number): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/suppliers/${id}`);
  }

  getCategories(): Observable<MasterItem[]> {
    return this.http.get<MasterItem[]>(`${API_BASE}/categories`);
  }

  createCategory(name: string): Observable<MasterItem> {
    return this.http.post<MasterItem>(`${API_BASE}/categories`, { name });
  }

  updateCategory(id: number, name: string): Observable<MasterItem> {
    return this.http.put<MasterItem>(`${API_BASE}/categories/${id}`, { name });
  }

  deleteCategory(id: number): Observable<void> {
    return this.http.delete<void>(`${API_BASE}/categories/${id}`);
  }
}
