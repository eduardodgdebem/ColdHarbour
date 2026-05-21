import { Routes } from '@angular/router';
import { HomePageComponent } from './features/home/pages/home-page/home-page.component';
import { PlaylistPageComponent } from './features/library/pages/playlist-page/playlist-page.component';
import { LoginPageComponent } from './features/auth/pages/login-page/login-page.component';
import { DevicesPageComponent } from './features/devices/pages/devices-page/devices-page.component';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    component: LoginPageComponent,
  },
  {
    path: 'home',
    component: HomePageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'playlist/:id',
    component: PlaylistPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'devices',
    component: DevicesPageComponent,
    canActivate: [authGuard],
  },
  {
    path: '',
    redirectTo: '/home',
    pathMatch: 'full',
  },
];
