export type SessionPermissions = {
  screenView: boolean;
  remoteControl: boolean;
  fileTransfer: boolean;
  clipboard: boolean;
  audio: boolean;
};

export type SessionRequest = {
  type: "session.request";
  requestId: string;
  targetDeviceId: string;
  viewerDeviceId: string;
  viewerUserId: string;
  requestedPermissions: SessionPermissions;
  createdAt: string;
};
