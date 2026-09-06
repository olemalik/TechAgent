import { InjectionToken } from '@angular/core';

export const TECHAGENT_API_URL = new InjectionToken<string>('TECHAGENT_API_URL', {
  factory: () => 'http://localhost:5073'
});

export const SYNCFUSION_LICENSE_KEY = new InjectionToken<string>('SYNCFUSION_LICENSE_KEY', {
  factory: () => ''
});
