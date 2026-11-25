import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  FiscalizeDevice,
  FiscalizeDeviceApiKey,
  DeviceConfig,
  CreateApiKeyRequest,
  SaveDeviceConfigRequest
} from '../../models/xero.models';

@Injectable({ providedIn: 'root' })
export class DevicesService {
  private http = inject(HttpClient);

  listDevices(): Observable<FiscalizeDevice[]> {
    return this.http.get<FiscalizeDevice[]>(`${environment.apiURL}Devices`);
  }

  listApiKeys(deviceId: number): Observable<FiscalizeDeviceApiKey[]> {
    return this.http.get<FiscalizeDeviceApiKey[]>(`${environment.apiURL}DeviceApiKeys/devices/${deviceId}`);
  }

  createApiKey(deviceId: number, req: CreateApiKeyRequest): Observable<FiscalizeDeviceApiKey> {
    return this.http.post<FiscalizeDeviceApiKey>(`${environment.apiURL}DeviceApiKeys/devices/${deviceId}`, req);
  }

  generateNewApiKey(apiKeyId: number): Observable<FiscalizeDeviceApiKey> {
    return this.http.post<FiscalizeDeviceApiKey>(`${environment.apiURL}DeviceApiKeys/${apiKeyId}/generate`, null);
  }

  revokeApiKey(apiKeyId: number): Observable<boolean> {
    return this.http.delete<boolean>(`${environment.apiURL}DeviceApiKeys/${apiKeyId}`);
  }

  testApiKey(apiKeyId: number): Observable<boolean> {
    return this.http.post<boolean>(`${environment.apiURL}DeviceApiKeys/${apiKeyId}/test`, null);
  }

  listConfigs(): Observable<DeviceConfig[]> {
    return this.http.get<DeviceConfig[]>(`${environment.apiURL}DeviceConfig`);
  }

  saveConfig(req: SaveDeviceConfigRequest): Observable<DeviceConfig> {
    return this.http.put<DeviceConfig>(`${environment.apiURL}DeviceConfig`, req);
  }

  setDefaultConfig(configId: number): Observable<DeviceConfig> {
    return this.http.post<DeviceConfig>(`${environment.apiURL}DeviceConfig/${configId}/set-default`, null);
  }

  deleteConfig(configId: number): Observable<void> {
    return this.http.delete<void>(`${environment.apiURL}DeviceConfig/${configId}`);
  }
}
