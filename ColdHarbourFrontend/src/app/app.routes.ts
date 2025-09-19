import { Routes } from '@angular/router';
import { PlaylistPageComponent } from './pages/playlist-page/playlist-page.component';

export const routes: Routes = [
    {
        path: 'playlist/:id',
        component: PlaylistPageComponent
    },
    {
        path: '',
        redirectTo: '/playlist/1',
        pathMatch: 'full'
    }
];
