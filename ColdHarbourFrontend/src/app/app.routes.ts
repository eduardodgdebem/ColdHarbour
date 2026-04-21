import { Routes } from '@angular/router';
import { PlaylistPageComponent } from './pages/playlist-page/playlist-page.component';
import { LoginPageComponent } from './pages/login-page/login-page.component';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    component: LoginPageComponent
  },
  {
    path: 'playlist/:id',
    component: PlaylistPageComponent,
    canActivate: [authGuard]
  },
  {
    path: '',
    redirectTo: '/playlist/1',
    pathMatch: 'full'
  }
];
